using ExoraFx.Api.Configuration;
using ExoraFx.Api.Helpers;
using ExoraFx.Api.Localization;
using ExoraFx.Api.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExoraFx.Api.Services.Bot;

public sealed class TelegramBotService(
    IUserSettingsStore store,
    IUserDefaultsResolver defaults,
    IBotLogService botLog,
    IBotLocalizer localizer,
    ITelegramBotClientProvider clientProvider,
    BotInputParser parser,
    BotMessageRenderer renderer,
    BotKeyboards keyboards,
    IConversionService conversion,
    IExchangeRateService rateService,
    IConversionHistoryStore history,
    PromptStateStore promptState,
    IOptions<TelegramSettings> telegramSettings,
    IOptions<ExchangeSettings> exchangeSettings,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery, UpdateType.InlineQuery];

    private static readonly string[] HelpAliases = ["/help", "помощь", "допомога", "help", "хелп"];
    private static readonly string[] RatesAliases = ["/rates", "курсы", "курси", "курс", "rates"];
    private static readonly string[] TableAliases = ["/table", "table", "таблица", "сетка", "сітка"];
    private static readonly string[] ScenarioAliases = ["/scenario", "scenario", "сценарий", "сценарій"];
    private static readonly string[] SettingsAliases = ["/settings", "settings", "настройки", "налаштування"];
    private static readonly string[] HistoryAliases = ["/history", "history", "история", "історія"];
    private static readonly string[] StatsAliases = ["/stats", "stats", "статистика", "статистики"];
    private static readonly string[] ResetAliases = ["reset", "default", "сброс", "дефолт", "скинути"];

    private const int HistoryPageSize = 5;

    private readonly TelegramBotClient? _bot = clientProvider.Client;
    private readonly TelegramSettings _telegram = telegramSettings.Value;
    private readonly ExchangeSettings _exchange = exchangeSettings.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (_bot is null)
        {
            logger.LogWarning("Telegram bot disabled: no token configured");
            botLog.LogEvent("bot disabled: no token configured");
            return;
        }

        User me;
        while (true)
        {
            try
            {
                me = await _bot.GetMe(ct);
                break;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram bot init failed, retry in 10s");
                botLog.LogEvent($"bot init failed: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        logger.LogInformation("Telegram bot @{Username} started", me.Username);
        botLog.LogEvent($"bot @{me.Username} started");

        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdates(offset, timeout: 30, allowedUpdates: AllowedUpdates, cancellationToken: ct);
                foreach (var upd in updates)
                {
                    offset = upd.Id + 1;
                    if (upd.Message?.Text is { } text)
                        await HandleMessage(upd.Message, text.Trim(), ct);
                    else if (upd.CallbackQuery is { } cq)
                        await HandleCallbackQuery(cq, ct);
                    else if (upd.InlineQuery is { } iq)
                        await HandleInlineQuery(iq, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram polling error");
                botLog.LogEvent($"polling error: {ex.Message}");
                await Task.Delay(5000, ct);
            }
        }
    }

    private async Task HandleMessage(Message msg, string text, CancellationToken ct)
    {
        var userId = msg.From?.Id;
        var isAdmin = _telegram.IsAdmin(userId);
        var tgLang = msg.From?.LanguageCode;
        var lang = defaults.Language(userId) ?? tgLang;
        botLog.LogIn(msg.Chat.Id, msg.From?.Username, userId, isAdmin, tgLang, text);

        if (userId is { } uid)
            store.RecordIdentity(uid, GetUserName(msg.From), GetRole(uid));

        if (userId is { } uid2)
        {
            string? pendingField = null;
            if (msg.ReplyToMessage is { MessageId: var replyToId, From.IsBot: true }
                && promptState.TryConsume(replyToId, uid2, out var byMsg))
            {
                pendingField = byMsg;
            }
            else if (!text.StartsWith('/') && promptState.TryConsumeForUser(uid2, out var byUser))
            {
                pendingField = byUser;
            }

            if (pendingField is not null)
            {
                await HandleCustomReply(msg, uid2, lang, pendingField, text, ct);
                return;
            }
        }

        try
        {
            var (reply, keyboard) = Route(text, userId, msg.From, tgLang, lang);
            await _bot!.SendMessage(msg.Chat.Id, reply, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
            botLog.LogOut(msg.Chat.Id, reply);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reply failed for chat={Chat} on: {Text}", msg.Chat.Id, text);
            botLog.LogEvent($"reply failed for chat={msg.Chat.Id} on \"{text}\": {ex.Message}");
        }
    }

    private (string Text, ReplyMarkup? Keyboard) Route(string input, long? userId, User? from, string? tgLang, string? lang)
    {
        var stripped = StripEmoji(input.Trim()).ToLowerInvariant();
        var keyboard = MainKeyboard(lang);

        if (stripped == "/start")
            return (Greeting(lang), keyboard);
        if (stripped == "/whoami")
            return (FormatWhoAmI(from, lang), keyboard);

        if (MatchesAny(stripped, HelpAliases))
            return (localizer.Get(BotKeys.HelpBody, lang), keyboard);
        if (MatchesAny(stripped, RatesAliases))
            return (renderer.FormatRates(userId, lang), keyboards.Rates(lang));

        if (TryStripPrefix(stripped, TableAliases, out _))
        {
            var t = renderer.BuildTable(userId, currency: null, bank: null, lang);
            return (t.Text, keyboards.TableLike(t.Currency, t.Bank, "tt", lang));
        }
        if (TryStripPrefix(stripped, ScenarioAliases, out _))
        {
            var s = renderer.BuildScenario(userId, currency: null, bank: null, lang);
            return (s.Text, keyboards.TableLike(s.Currency, s.Bank, "ts", lang));
        }
        if (TryStripPrefix(stripped, SettingsAliases, out var settingsRest))
            return HandleSettingsText(userId, settingsRest, tgLang, lang);
        if (TryStripPrefix(stripped, HistoryAliases, out _))
        {
            var (htext, hkb) = HandleHistory(userId, lang, offset: 0, fromMenu: false);
            return (htext, (ReplyMarkup?)hkb ?? keyboard);
        }
        if (TryStripPrefix(stripped, StatsAliases, out _))
        {
            if (userId is not { } sid)
                return (localizer.Get(BotKeys.HistoryUserUnknown, lang), keyboard);
            var stats = history.GetStats(sid);
            var total = history.Count(sid);
            ReplyMarkup statsKb = total == 0 ? keyboard : keyboards.Stats(StatsPeriod.All, lang, includeBack: false);
            return (renderer.FormatHistoryStats(stats, StatsPeriod.All, lang), statsKb);
        }

        return RunCalculation(input.Trim(), userId, from, lang, keyboard);
    }

    private (string Text, ReplyMarkup? Keyboard) RunCalculation(string text, long? userId, User? from, string? lang, ReplyMarkup fallback)
    {
        var outcome = parser.ParseCalculation(text, userId);
        if (outcome is ParseOutcome.Success s)
        {
            var showHint = defaults.ShowBestHint(userId);
            var result = MaybeUseBestBank(s.Result, s.Direction, s.BankExplicit, showHint);

            long entryId = 0;
            if (userId is { } uid && defaults.HistoryEnabled(uid))
            {
                try
                {
                    entryId = history.Append(uid, GetUserName(from), GetRole(uid), result);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to append history for user={User}", uid);
                    botLog.LogEvent($"history append failed user={uid}: {ex.Message}");
                }
            }

            ReplyMarkup? markup = keyboards.Convert(result, s.Direction, lang, entryId, expanded: false, isDone: false);
            return (renderer.FormatConvert(result, s.MarginOverridden, lang, showHint), markup ?? fallback);
        }

        return outcome switch
        {
            ParseOutcome.Error e => (localizer.Get(e.Key, lang, e.Args), (ReplyMarkup?)fallback),
            _ => (localizer.Get(BotKeys.UnknownBody, lang), (ReplyMarkup?)fallback),
        };
    }

    private ConvertResult MaybeUseBestBank(ConvertResult original, ConversionDirection direction, bool bankExplicit, bool showHint)
    {
        if (bankExplicit || !showHint || direction == ConversionDirection.Cross)
            return original;

        var bestBank = rateService.FindBestBank(original.From, original.To);
        if (bestBank is null || bestBank == original.Bank)
            return original;

        var foreign = (direction == ConversionDirection.Reverse ? original.To : original.From).ToLowerInvariant();
        var recalc = direction == ConversionDirection.Reverse
            ? conversion.ConvertReverse(foreign, original.ToAmount, bestBank, original.MarginPercent)
            : conversion.Convert(foreign, CurrencyHelper.Uah, original.FromAmount, bestBank, original.MarginPercent);
        return recalc ?? original;
    }

    private (string Text, InlineKeyboardMarkup? Keyboard) HandleHistory(long? userId, string? lang, int offset, bool fromMenu)
    {
        InlineKeyboardMarkup? backKb = fromMenu ? keyboards.BackToHistory(lang) : null;

        if (userId is null)
            return (localizer.Get(BotKeys.HistoryUserUnknown, lang), backKb);

        var total = history.Count(userId.Value);
        if (total == 0)
            return (localizer.Get(BotKeys.HistoryEmpty, lang), backKb);

        var lastPageOffset = ((total - 1) / HistoryPageSize) * HistoryPageSize;
        var snapped = Math.Clamp(offset, 0, lastPageOffset);
        var safeOffset = snapped - (snapped % HistoryPageSize);
        var entries = history.GetPage(userId.Value, safeOffset, HistoryPageSize);
        if (entries.Count == 0)
            return (localizer.Get(BotKeys.HistoryEmpty, lang), backKb);

        var rows = entries.Select((e, i) =>
        {
            var profitTail = e.ProfitEur is { } eur
                ? $"{CurrencyHelper.FormatAmount(e.ProfitUah, 2)} ₴ / {CurrencyHelper.FormatAmount(eur, 2)} €"
                : $"{CurrencyHelper.FormatAmount(e.ProfitUah, 2)} ₴";
            var marker = e.State == HistoryState.Done ? "🟢" : "⏳";
            return
                $"{marker} *#{i + 1}* `{e.CreatedAtUtc.ToLocalTime():HH:mm}` " +
                $"{CurrencyHelper.FormatAmount(e.FromAmount, 2)} {e.FromCurrency} → " +
                $"{CurrencyHelper.FormatAmount(e.ToAmount, 2)} {e.ToCurrency}\n" +
                $"_{e.Bank}, {CurrencyHelper.FormatAmount(e.MarginPercent, 2)}%, +{profitTail}_";
        });

        var first = safeOffset + 1;
        var last = safeOffset + entries.Count;
        var header = localizer.Get(BotKeys.HistoryPageHeader, lang, first, last, total);
        var keyboard = keyboards.History(entries, safeOffset, HistoryPageSize, total, lang, includeBack: fromMenu);
        return (header + "\n\n" + string.Join("\n\n", rows), keyboard);
    }

    private async Task UpdateHistoryStatsScreen(CallbackQuery cq, long userId, string period, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var (since, until) = StatsPeriod.Bounds(period);
        var stats = history.GetStats(userId, since, until);
        var totalAcrossAll = history.Count(userId);
        var text = renderer.FormatHistoryStats(stats, period, lang);
        var keyboard = totalAcrossAll == 0 ? keyboards.StatsBackOnly(lang) : keyboards.Stats(period, lang, includeBack: true);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, text, keyboard, ct);
    }

    private (string Text, ReplyMarkup? Keyboard) HandleSettingsText(long? userId, string rest, string? tgLang, string? lang)
    {
        if (userId is null)
            return (localizer.Get(BotKeys.SettingsUserUnknown, lang), MainKeyboard(lang));

        rest = rest.Trim();

        if (string.IsNullOrEmpty(rest))
            return (RenderSettingsMain(userId.Value, lang), keyboards.SettingsMain(store.Get(userId.Value), lang));

        if (MatchesAny(rest.ToLowerInvariant(), ResetAliases))
        {
            store.Reset(userId.Value);
            return (localizer.Get(BotKeys.SettingsResetAll, lang), MainKeyboard(tgLang));
        }

        return (localizer.Get(BotKeys.SettingsUseMenu, lang), MainKeyboard(lang));
    }

    private async Task HandleInlineQuery(InlineQuery iq, CancellationToken ct)
    {
        var query = iq.Query?.Trim() ?? "";
        var lang = defaults.Language(iq.From.Id) ?? iq.From.LanguageCode;

        if (string.IsNullOrEmpty(query))
        {
            await _bot!.AnswerInlineQuery(iq.Id, [], cacheTime: 0, cancellationToken: ct);
            return;
        }

        var outcome = parser.ParseCalculation(query, iq.From.Id);
        Telegram.Bot.Types.InlineQueryResults.InlineQueryResult[] results;

        if (outcome is ParseOutcome.Success s)
        {
            var showHint = defaults.ShowBestHint(iq.From.Id);
            var resultIq = MaybeUseBestBank(s.Result, s.Direction, s.BankExplicit, showHint);
            var text = renderer.FormatConvert(resultIq, s.MarginOverridden, lang, showHint);
            var title = $"{CurrencyHelper.FormatAmount(resultIq.FromAmount, 2)} {resultIq.From} → {CurrencyHelper.FormatAmount(resultIq.ToAmount, 2)} {resultIq.To}";
            var description = $"{resultIq.Bank} · margin {CurrencyHelper.FormatAmount(resultIq.MarginPercent, 2)}% · profit {CurrencyHelper.FormatAmount(resultIq.ProfitUah, 2)} ₴";
            results =
            [
                new Telegram.Bot.Types.InlineQueryResults.InlineQueryResultArticle(
                    id: "ok",
                    title: title,
                    inputMessageContent: new Telegram.Bot.Types.InlineQueryResults.InputTextMessageContent(text)
                    {
                        ParseMode = ParseMode.Markdown,
                    })
                {
                    Description = description,
                },
            ];
        }
        else if (outcome is ParseOutcome.Error e)
        {
            var msg = localizer.Get(e.Key, lang, e.Args);
            results =
            [
                new Telegram.Bot.Types.InlineQueryResults.InlineQueryResultArticle(
                    id: "err",
                    title: "❌ " + MarkdownHelper.Strip(msg),
                    inputMessageContent: new Telegram.Bot.Types.InlineQueryResults.InputTextMessageContent(msg)
                    {
                        ParseMode = ParseMode.Markdown,
                    }),
            ];
        }
        else
        {
            results = [];
        }

        await _bot!.AnswerInlineQuery(iq.Id, results, cacheTime: 0, cancellationToken: ct);
    }

    private async Task HandleCallbackQuery(CallbackQuery cq, CancellationToken ct)
    {
        var userId = cq.From.Id;
        var chatId = cq.Message?.Chat.Id;
        var messageId = cq.Message?.MessageId;
        var lang = defaults.Language(userId) ?? cq.From.LanguageCode;
        botLog.LogIn(chatId ?? 0, cq.From.Username, userId, _telegram.IsAdmin(userId), cq.From.LanguageCode, $"[callback] {cq.Data}");

        store.RecordIdentity(userId, GetUserName(cq.From), GetRole(userId));

        var data = CallbackData.Parse(cq.Data ?? "");

        try
        {
            switch (data)
            {
                case CallbackData.ConvertBank cb:
                    var bankToUse = cb.NewBank == "d" ? defaults.Bank(userId) : cb.NewBank;
                    await RerenderConvertMessage(cq, lang, cb.Direction, cb.Foreign, cb.Amount, bankToUse, cb.CurrentMargin, cb.EntryId, cb.IsDone, expanded: true, marginOverridden: true, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.ConvertMargin cm:
                    var marginToUse = cm.NewMarginToken == "d"
                        ? defaults.Margin(userId)
                        : CurrencyHelper.TryParseDecimal(cm.NewMarginToken, out var newM) ? newM : cm.CurrentMargin;
                    await RerenderConvertMessage(cq, lang, cm.Direction, cm.Foreign, cm.Amount, cm.Bank, marginToUse, cm.EntryId, cm.IsDone, expanded: true, marginOverridden: true, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.ConvertView cv:
                    await RerenderConvertMessage(cq, lang, cv.Direction, cv.Foreign, cv.Amount, cv.Bank, cv.Margin, cv.EntryId, cv.IsDone, expanded: cv.Expanded, marginOverridden: false, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
                    break;

                case CallbackData.ConvertMark cmk:
                    var newEntryState = history.ToggleState(userId, cmk.EntryId);
                    if (newEntryState is null)
                    {
                        logger.LogWarning("Mark: entry {EntryId} not found for user {UserId}", cmk.EntryId, userId);
                        await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbNotFound, lang), showAlert: true, cancellationToken: ct);
                        break;
                    }
                    var nowDone = newEntryState == HistoryState.Done;
                    await RerenderConvertMessage(cq, lang, cmk.Direction, cmk.Foreign, cmk.Amount, cmk.Bank, cmk.Margin, cmk.EntryId, nowDone, expanded: cmk.Expanded, marginOverridden: false, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(nowDone ? BotKeys.CbMarked : BotKeys.CbUnmarked, lang), cancellationToken: ct);
                    break;

                case CallbackData.RatesRefresh:
                    await rateService.RefreshAllAsync(ct);
                    if (chatId is { } cid && messageId is { } mid)
                        await TryEditMessage(cid, mid, renderer.FormatRates(userId, lang), keyboards.Rates(lang), ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbRefreshed, lang), cancellationToken: ct);
                    break;

                case CallbackData.SettingsOpen so:
                    await OpenSettingsScreen(cq, userId, lang, so.Field, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
                    break;

                case CallbackData.SettingsApply sa:
                    await ApplySettingsCallback(cq, userId, lang, sa.Field, sa.Value, ct);
                    break;

                case CallbackData.SettingsCustomPrompt scp:
                    await SendCustomPrompt(cq, scp.Field, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
                    break;

                case CallbackData.SettingsResetAll:
                    store.Reset(userId);
                    await UpdateSettingsMainScreen(cq, userId, defaults.Language(userId) ?? cq.From.LanguageCode, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.TableModify tm:
                    await UpdateTableScreen(cq, userId, tm.Currency, tm.Bank, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.ScenarioModify sm:
                    await UpdateScenarioScreen(cq, userId, sm.Currency, sm.Bank, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.HistoryPage hp:
                    await UpdateHistoryScreen(cq, userId, hp.Offset, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
                    break;

                case CallbackData.HistoryClear:
                    history.Clear(userId);
                    await OpenSettingsScreen(cq, userId, lang, "history", ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbHistoryCleared, lang), cancellationToken: ct);
                    break;

                case CallbackData.HistoryToggle ht:
                    store.SetHistoryEnabled(userId, ht.Enabled);
                    await OpenSettingsScreen(cq, userId, lang, "history", ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(ht.Enabled ? BotKeys.CbHistoryRecordingOn : BotKeys.CbHistoryRecordingOff, lang), cancellationToken: ct);
                    break;

                case CallbackData.BestHintToggle bh:
                    store.SetShowBestHint(userId, bh.Enabled);
                    await UpdateSettingsMainScreen(cq, userId, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(bh.Enabled ? BotKeys.CbBestHintOn : BotKeys.CbBestHintOff, lang), cancellationToken: ct);
                    break;

                case CallbackData.HistoryStatsOpen hso:
                    await UpdateHistoryStatsScreen(cq, userId, hso.Period, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
                    break;

                case CallbackData.HistoryEntryToggle het:
                    var newState = history.ToggleState(userId, het.EntryId);
                    if (newState is null)
                    {
                        logger.LogWarning("History toggle: entry {EntryId} not found for user {UserId}", het.EntryId, userId);
                        botLog.LogEvent($"history toggle miss: entry {het.EntryId} not found for user {userId}");
                        await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbNotFound, lang), showAlert: true, cancellationToken: ct);
                        break;
                    }
                    await UpdateHistoryScreen(cq, userId, het.PageOffset, lang, ct);
                    await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(newState == HistoryState.Done ? BotKeys.CbDoneOn : BotKeys.CbDoneOff, lang), cancellationToken: ct);
                    break;

                default:
                    await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Callback failed for user={User} data={Data}", userId, cq.Data);
            botLog.LogEvent($"callback failed user={userId} data=\"{cq.Data}\": {ex.Message}");
            try
            {
                await _bot!.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
            }
            catch
            {
            }
        }
    }

    private async Task RerenderConvertMessage(CallbackQuery cq, string? lang, char direction, string foreign, decimal amount, string bank, decimal margin, long entryId, bool isDone, bool expanded, bool marginOverridden, CancellationToken ct)
    {
        var dir = direction == 'r' ? ConversionDirection.Reverse : ConversionDirection.Forward;
        var result = dir == ConversionDirection.Reverse
            ? conversion.ConvertReverse(foreign, amount, bank, margin)
            : conversion.Convert(foreign, CurrencyHelper.Uah, amount, bank, margin);

        if (result is null || cq.Message is not { } msg)
            return;

        var text = renderer.FormatConvert(result, marginOverridden, lang, defaults.ShowBestHint(cq.From.Id));
        var keyboard = keyboards.Convert(result, dir, lang, entryId, expanded, isDone);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, text, keyboard, ct);
        botLog.LogOut(msg.Chat.Id, text);
    }

    private async Task UpdateTableScreen(CallbackQuery cq, long userId, string currency, string bank, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var t = renderer.BuildTable(userId, currency, bank, lang);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, t.Text, keyboards.TableLike(t.Currency, t.Bank, "tt", lang), ct);
    }

    private async Task UpdateScenarioScreen(CallbackQuery cq, long userId, string currency, string bank, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var s = renderer.BuildScenario(userId, currency, bank, lang);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, s.Text, keyboards.TableLike(s.Currency, s.Bank, "ts", lang), ct);
    }

    private async Task UpdateHistoryScreen(CallbackQuery cq, long userId, int offset, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var (text, keyboard) = HandleHistory(userId, lang, offset, fromMenu: true);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, text, keyboard, ct);
    }

    private async Task OpenSettingsScreen(CallbackQuery cq, long userId, string? lang, string field, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var s = store.Get(userId);
        var (text, keyboard) = field switch
        {
            "menu" => (RenderSettingsMain(userId, lang), keyboards.SettingsMain(s, lang)),
            "lang" => (localizer.Get(BotKeys.SettingsTitleLang, lang, FormatValueOrDefault(s.Language, lang ?? "—", lang)), keyboards.SettingsLang(s, lang)),
            "bank" => (localizer.Get(BotKeys.SettingsTitleBank, lang, FormatValueOrDefault(s.DefaultBank is null ? null : keyboards.FullBank(s.DefaultBank, lang), keyboards.FullBank(_exchange.DefaultBank, lang), lang)), keyboards.SettingsBank(s, lang)),
            "currency" => (localizer.Get(BotKeys.SettingsTitleCurrency, lang, FormatValueOrDefault(s.DefaultCurrency?.ToUpperInvariant(), "EUR", lang)), keyboards.SettingsCurrency(s, lang)),
            "margin" => (localizer.Get(BotKeys.SettingsTitleMargin, lang, FormatValueOrDefault(s.MarginPercent is { } m ? CurrencyHelper.FormatAmount(m, 2) + "%" : null, CurrencyHelper.FormatAmount(_exchange.MarginPercent, 2) + "%", lang)), keyboards.SettingsMargin(s, lang)),
            "amount" => (localizer.Get(BotKeys.SettingsTitleAmount, lang, FormatValueOrDefault(s.DefaultAmount is { } a ? CurrencyHelper.FormatAmount(a, 2) : null, "100", lang)), keyboards.SettingsAmount(s, lang)),
            "history" => RenderHistorySettings(s, userId, lang),
            _ => (RenderSettingsMain(userId, lang), keyboards.SettingsMain(s, lang)),
        };

        await TryEditMessage(msg.Chat.Id, msg.MessageId, text, keyboard, ct);
    }

    private (string Text, InlineKeyboardMarkup Keyboard) RenderHistorySettings(UserSettings s, long userId, string? lang)
    {
        var enabled = s.HistoryEnabled ?? true;
        var statusKey = enabled ? BotKeys.HistoryStatusOn : BotKeys.HistoryStatusOff;
        var text = localizer.Get(BotKeys.SettingsTitleHistory, lang, history.Count(userId), localizer.Get(statusKey, lang));
        return (text, keyboards.SettingsHistory(s, lang));
    }

    private async Task ApplySettingsCallback(CallbackQuery cq, long userId, string? lang, string field, string value, CancellationToken ct)
    {
        var resetField = value == "d";

        switch (field)
        {
            case "lang":
                if (resetField)
                    store.ResetField(userId, UserSettingsField.Language);
                else
                    store.TrySetLanguage(userId, value);
                lang = defaults.Language(userId) ?? cq.From.LanguageCode;
                break;
            case "bank":
                if (resetField)
                    store.ResetField(userId, UserSettingsField.DefaultBank);
                else
                    store.TrySetBank(userId, value);
                break;
            case "currency":
                if (resetField)
                    store.ResetField(userId, UserSettingsField.DefaultCurrency);
                else
                    store.TrySetCurrency(userId, value);
                break;
            case "margin":
                if (resetField)
                    store.ResetField(userId, UserSettingsField.MarginPercent);
                else if (CurrencyHelper.TryParseDecimal(value, out var mPct))
                    store.TrySetMargin(userId, mPct);
                break;
            case "amount":
                if (resetField)
                    store.ResetField(userId, UserSettingsField.DefaultAmount);
                else if (CurrencyHelper.TryParseDecimal(value, out var amt))
                    store.TrySetAmount(userId, amt);
                break;
        }

        await UpdateSettingsMainScreen(cq, userId, lang, ct);
        await _bot!.AnswerCallbackQuery(cq.Id, localizer.Get(BotKeys.CbAck, lang), cancellationToken: ct);
    }

    private async Task UpdateSettingsMainScreen(CallbackQuery cq, long userId, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var text = RenderSettingsMain(userId, lang);
        await TryEditMessage(msg.Chat.Id, msg.MessageId, text, keyboards.SettingsMain(store.Get(userId), lang), ct);
    }

    private async Task TryEditMessage(long chatId, int messageId, string text, InlineKeyboardMarkup? keyboard, CancellationToken ct)
    {
        try
        {
            await _bot!.EditMessageText(chatId, messageId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        }
        catch (Exception ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private async Task SendCustomPrompt(CallbackQuery cq, string field, string? lang, CancellationToken ct)
    {
        if (cq.Message is not { } msg)
            return;

        var promptKey = field switch
        {
            "margin" => BotKeys.SettingsPromptMargin,
            "amount" => BotKeys.SettingsPromptAmount,
            _ => BotKeys.SettingsPromptAmount,
        };
        var prompt = localizer.Get(promptKey, lang);
        var sent = await _bot!.SendMessage(msg.Chat.Id, prompt, parseMode: ParseMode.Markdown, replyMarkup: new ForceReplyMarkup { Selective = true }, cancellationToken: ct);
        promptState.Remember(sent.MessageId, cq.From.Id, field);
    }

    private async Task HandleCustomReply(Message msg, long userId, string? lang, string field, string text, CancellationToken ct)
    {
        var trimmed = text.TrimEnd('%').Trim();
        decimal parsed = 0m;
        var ok = field switch
        {
            "margin" => CurrencyHelper.TryParseDecimal(trimmed, out parsed) && store.TrySetMargin(userId, parsed),
            "amount" => CurrencyHelper.TryParseDecimal(trimmed, out parsed) && store.TrySetAmount(userId, parsed),
            _ => false,
        };

        if (ok)
        {
            var display = field == "margin"
                ? CurrencyHelper.FormatAmount(parsed, 2) + "%"
                : CurrencyHelper.FormatAmount(parsed, 2);
            var ack = localizer.Get(BotKeys.SettingsCustomApplied, lang, display);
            await _bot!.SendMessage(msg.Chat.Id, ack, parseMode: ParseMode.Markdown, cancellationToken: ct);
            await _bot!.SendMessage(msg.Chat.Id, RenderSettingsMain(userId, lang), parseMode: ParseMode.Markdown, replyMarkup: keyboards.SettingsMain(store.Get(userId), lang), cancellationToken: ct);
        }
        else
        {
            await _bot!.SendMessage(msg.Chat.Id, localizer.Get(BotKeys.SettingsCustomInvalid, lang, text), parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
    }

    private string RenderSettingsMain(long userId, string? lang)
    {
        var s = store.Get(userId);
        return localizer.Get(
            BotKeys.SettingsCurrent,
            lang,
            FormatValueOrDefault(s.Language, lang ?? "—", lang),
            FormatValueOrDefault(s.MarginPercent is { } m ? CurrencyHelper.FormatAmount(m, 2) + "%" : null, CurrencyHelper.FormatAmount(_exchange.MarginPercent, 2) + "%", lang),
            FormatValueOrDefault(s.DefaultBank is null ? null : keyboards.FullBank(s.DefaultBank, lang), keyboards.FullBank(_exchange.DefaultBank, lang), lang),
            FormatValueOrDefault(s.DefaultCurrency?.ToUpperInvariant(), "EUR", lang),
            FormatValueOrDefault(s.DefaultAmount is { } a ? CurrencyHelper.FormatAmount(a, 2) : null, "100", lang));
    }

    private string FormatValueOrDefault(string? userValue, string defaultDisplay, string? lang) =>
        userValue is not null
            ? $"*{userValue}*"
            : localizer.Get(BotKeys.SettingsValueDefault, lang, defaultDisplay);

    private string FormatWhoAmI(User? from, string? lang)
    {
        if (from is null)
            return localizer.Get(BotKeys.WhoamiUserUnknown, lang);

        var role = _telegram.IsAdmin(from.Id)
            ? localizer.Get(BotKeys.WhoamiRoleAdmin, lang)
            : localizer.Get(BotKeys.WhoamiRoleUser, lang);
        var name = from.Username is { Length: > 0 } u
            ? "@" + MarkdownHelper.Escape(u)
            : MarkdownHelper.Escape($"{from.FirstName} {from.LastName}".Trim());
        return localizer.Get(BotKeys.WhoamiBody, lang, from.Id, name, role);
    }

    private string Greeting(string? lang) => localizer.Get(BotKeys.GreetingBody, lang);

    private string GetRole(long userId) => _telegram.IsAdmin(userId) ? "admin" : "user";

    private static string? GetUserName(User? from)
    {
        if (from is null)
            return null;
        if (!string.IsNullOrEmpty(from.Username))
            return "@" + from.Username;
        var full = $"{from.FirstName} {from.LastName}".Trim();
        return string.IsNullOrEmpty(full) ? null : full;
    }

    private ReplyKeyboardMarkup MainKeyboard(string? lang) =>
        new(
            [
                [new KeyboardButton(localizer.Get(BotKeys.KbRates, lang)), new KeyboardButton(localizer.Get(BotKeys.KbSettings, lang))],
                [new KeyboardButton(localizer.Get(BotKeys.KbHelp, lang))],
            ])
        {
            ResizeKeyboard = true,
            IsPersistent = true,
        };

    private static bool MatchesAny(string input, string[] commands)
    {
        foreach (var cmd in commands)
        {
            if (input.Equals(cmd, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryStripPrefix(string input, string[] commands, out string rest)
    {
        foreach (var cmd in commands)
        {
            if (input.Equals(cmd, StringComparison.OrdinalIgnoreCase))
            {
                rest = "";
                return true;
            }

            if (input.StartsWith(cmd + " ", StringComparison.OrdinalIgnoreCase))
            {
                rest = input[(cmd.Length + 1)..].TrimStart();
                return true;
            }
        }

        rest = "";
        return false;
    }

    private static string StripEmoji(string text)
    {
        var span = text.AsSpan();
        var i = 0;
        while (i < span.Length && !char.IsLetter(span[i]) && span[i] != '/')
            i++;

        return span[i..].Trim().ToString();
    }
}
