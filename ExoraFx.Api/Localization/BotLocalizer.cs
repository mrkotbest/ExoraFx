using System.Globalization;

namespace ExoraFx.Api.Localization;

public sealed class BotLocalizer : IBotLocalizer
{
    public const string DefaultLanguage = "ru";

    private static readonly string[] Supported = ["ru", "uk", "en"];

    private static readonly Dictionary<string, string> InputAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ua"] = "uk",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = Build();

    public string Get(string key, string? languageCode, params object[] args)
    {
        var lang = ResolveLanguage(languageCode);
        var template = Strings.TryGetValue(key, out var byLang)
            ? byLang.GetValueOrDefault(lang) ?? byLang.GetValueOrDefault(DefaultLanguage) ?? key
            : key;

        return args.Length == 0
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    public string ResolveLanguage(string? languageCode)
    {
        if (languageCode is null)
            return DefaultLanguage;

        var dash = languageCode.IndexOf('-');
        var prefix = (dash < 0 ? languageCode : languageCode[..dash]).ToLowerInvariant();

        if (InputAliases.TryGetValue(prefix, out var alias))
            return alias;

        return Array.IndexOf(Supported, prefix) >= 0 ? prefix : DefaultLanguage;
    }

    public bool IsSupportedInput(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var prefix = languageCode.Trim().ToLowerInvariant();
        return InputAliases.ContainsKey(prefix) || Array.IndexOf(Supported, prefix) >= 0;
    }

    private static Dictionary<string, Dictionary<string, string>> Build() => new()
    {
        [BotKeys.GreetingBody] = Trio(
            ru: "👋 Готов считать.\n\nПример: `100 евро` или просто `евро`.\nВсё остальное — /help.",
            uk: "👋 Готовий рахувати.\n\nПриклад: `100 євро` або просто `євро`.\nВсе інше — /help.",
            en: "👋 Ready.\n\nExample: `100 eur` or just `eur`.\nMore — /help."),

        [BotKeys.HelpBody] = Trio(
            ru: "*Ввод*\n" +
                "`100 eur` — 100 EUR → UAH с твоей маржой и банком\n" +
                "`100 eur mono` — указать банк: `mono` / `privat` / `nbu`\n" +
                "`100 eur 8%` — маржа на этот расчёт\n" +
                "`100 eur mono 8%` — банк и маржа сразу\n" +
                "`100 eur usd` — кросс EUR → USD\n" +
                "`eur` — расчёт на сумму из /settings\n" +
                "`5000 uah` — обратный: сколько валюты дать за 5000 ₴\n" +
                "`5к eur` — `к` / `K` = тысячи (`1.5к` = 1500)\n\n" +
                "_Слова `евро`, `доллар`, `грн`, `злотый` тоже распознаются._\n\n" +
                "*Команды*\n" +
                "/history — последние расчёты\n" +
                "/rates — курсы по банкам\n" +
                "/scenario — таблица маржей\n" +
                "/settings — твои настройки\n" +
                "/stats — статистика по сделкам\n" +
                "/table — таблица сумм\n" +
                "/whoami — id и роль",
            uk: "*Ввід*\n" +
                "`100 eur` — 100 EUR → UAH з твоєю маржею і банком\n" +
                "`100 eur mono` — вказати банк: `mono` / `privat` / `nbu`\n" +
                "`100 eur 8%` — маржа на цей розрахунок\n" +
                "`100 eur mono 8%` — банк і маржа разом\n" +
                "`100 eur usd` — крос EUR → USD\n" +
                "`eur` — розрахунок на суму з /settings\n" +
                "`5000 uah` — зворотний: скільки валюти дати за 5000 ₴\n" +
                "`5к eur` — `к` / `K` = тисячі (`1.5к` = 1500)\n\n" +
                "_Слова `євро`, `долар`, `грн`, `злотий` теж розпізнаються._\n\n" +
                "*Команди*\n" +
                "/history — останні розрахунки\n" +
                "/rates — курси за банками\n" +
                "/scenario — таблиця маржі\n" +
                "/settings — твої налаштування\n" +
                "/stats — статистика угод\n" +
                "/table — таблиця сум\n" +
                "/whoami — id та роль",
            en: "*Input*\n" +
                "`100 eur` — 100 EUR → UAH with your margin and bank\n" +
                "`100 eur mono` — pick a bank: `mono` / `privat` / `nbu`\n" +
                "`100 eur 8%` — per-trade margin\n" +
                "`100 eur mono 8%` — bank and margin at once\n" +
                "`100 eur usd` — cross EUR → USD\n" +
                "`eur` — calc on amount from /settings\n" +
                "`5000 uah` — reverse: how much foreign for 5000 ₴\n" +
                "`5k eur` — `k` / `K` = thousands (`1.5k` = 1500)\n\n" +
                "*Commands*\n" +
                "/history — recent conversions\n" +
                "/rates — bank rates\n" +
                "/scenario — margin table\n" +
                "/settings — your settings\n" +
                "/stats — trade statistics\n" +
                "/table — amount table\n" +
                "/whoami — id and role"),

        [BotKeys.UnknownBody] = Trio(
            ru: "🤔 Не разобрал, что нужно посчитать.\n\n" +
                "Попробуй так:\n" +
                "• `100 евро` — посчитать конкретную сумму\n" +
                "• `евро` — расчёт на твою дефолтную сумму\n" +
                "• `5к грн` — обратный расчёт (сколько валюты дать за 5000 ₴)\n\n" +
                "Полная шпаргалка — /help",
            uk: "🤔 Не зрозумів, що потрібно порахувати.\n\n" +
                "Спробуй так:\n" +
                "• `100 євро` — порахувати конкретну суму\n" +
                "• `євро` — розрахунок на твою дефолтну суму\n" +
                "• `5к грн` — зворотний розрахунок (скільки валюти дати за 5000 ₴)\n\n" +
                "Повна шпаргалка — /help",
            en: "🤔 Didn't catch what to calculate.\n\n" +
                "Try one of these:\n" +
                "• `100 eur` — calculate a specific amount\n" +
                "• `eur` — use your default amount from settings\n" +
                "• `5к uah` — reverse calc (how much foreign for 5000 UAH)\n\n" +
                "Full cheatsheet — /help"),

        [BotKeys.ParseUnknownToken] = Trio(ru: "Не понял: `{0}`", uk: "Не зрозумів: `{0}`", en: "Didn't recognise: `{0}`"),
        [BotKeys.ParseTooManyCurrencies] = Trio(ru: "Слишком много валют: `{0}`", uk: "Забагато валют: `{0}`", en: "Too many currencies: `{0}`"),
        [BotKeys.ParseSameCurrency] = Trio(ru: "Одинаковые валюты — нечего считать.", uk: "Однакові валюти — нема що рахувати.", en: "Same currency — nothing to convert."),
        [BotKeys.ParseRateNotLoaded] = Trio(ru: "Курс {0} → {1} не загружен.", uk: "Курс {0} → {1} не завантажений.", en: "Rate {0} → {1} not loaded."),
        [BotKeys.ParseMarginOutOfRange] = Trio(ru: "Маржа {0}..{1}%: `{2}`", uk: "Маржа {0}..{1}%: `{2}`", en: "Margin {0}..{1}%: `{2}`"),

        [BotKeys.ConvertOfficialLabel] = Trio(ru: "Официал", uk: "Офіціал", en: "Official"),
        [BotKeys.ConvertOurRateLabel] = Trio(ru: "Наш курс", uk: "Наш курс", en: "Our rate"),
        [BotKeys.ConvertMarginLabel] = Trio(ru: "Маржа", uk: "Маржа", en: "Margin"),
        [BotKeys.ConvertProfitLabel] = Trio(ru: "Прибыль", uk: "Прибуток", en: "Profit"),
        [BotKeys.ConvertBankLabel] = Trio(ru: "Банк", uk: "Банк", en: "Bank"),
        [BotKeys.ConvertStaleSuffix] = Trio(ru: " _(курс устарел)_", uk: " _(курс застарів)_", en: " _(stale rate)_"),
        [BotKeys.ConvertOverriddenSuffix] = Trio(ru: "  _(переопределено)_", uk: "  _(перевизначено)_", en: "  _(overridden)_"),
        [BotKeys.ConvertBestHintBetter] = Trio(ru: "🏆 Лучше через `{0}` (+{1} ₴)", uk: "🏆 Краще через `{0}` (+{1} ₴)", en: "🏆 Better via `{0}` (+{1} ₴)"),
        [BotKeys.ConvertBestHintBetterReverse] = Trio(ru: "🏆 Лучше через `{0}`", uk: "🏆 Краще через `{0}`", en: "🏆 Better via `{0}`"),

        [BotKeys.WhoamiUserUnknown] = Trio(ru: "Не смог определить пользователя.", uk: "Не вдалося визначити користувача.", en: "Couldn't identify user."),
        [BotKeys.WhoamiRoleAdmin] = Trio(ru: "админ", uk: "адмін", en: "admin"),
        [BotKeys.WhoamiRoleUser] = Trio(ru: "пользователь", uk: "користувач", en: "user"),
        [BotKeys.WhoamiBody] = Trio(ru: "id: `{0}`\nИмя: {1}\nРоль: {2}", uk: "id: `{0}`\nІм'я: {1}\nРоль: {2}", en: "id: `{0}`\nName: {1}\nRole: {2}"),

        [BotKeys.RatesTitle] = Trio(ru: "*Курсы* (маржа {0}%)", uk: "*Курси* (маржа {0}%)", en: "*Rates* (margin {0}%)"),
        [BotKeys.RatesEmpty] = Trio(ru: "Курсы ещё не загружены — попробуй через минуту.", uk: "Курси ще не завантажилися — спробуй за хвилину.", en: "Rates not loaded — try again in a minute."),

        [BotKeys.TableNoRate] = Trio(ru: "Курс {0} не загружен.", uk: "Курс {0} не завантажений.", en: "Rate for {0} not loaded."),
        [BotKeys.TableHeader] = Trio(ru: "{0} *Сетка {1}* · `{2}` · {3}%", uk: "{0} *Сітка {1}* · `{2}` · {3}%", en: "{0} *{1} grid* · `{2}` · {3}%"),
        [BotKeys.TableColAmount] = Trio(ru: "Сумма", uk: "Сума", en: "Amt"),
        [BotKeys.TableColResult] = Trio(ru: "UAH", uk: "UAH", en: "UAH"),
        [BotKeys.TableColProfit] = Trio(ru: "Прибыль ₴/€", uk: "Прибуток ₴/€", en: "Profit ₴/€"),

        [BotKeys.ScenarioNoRate] = Trio(ru: "Курс {0} не загружен.", uk: "Курс {0} не завантажений.", en: "Rate for {0} not loaded."),
        [BotKeys.ScenarioHeader] = Trio(ru: "{0} *Сценарий {1} {2}* · `{3}`", uk: "{0} *Сценарій {1} {2}* · `{3}`", en: "{0} *{1} {2} scenario* · `{3}`"),
        [BotKeys.ScenarioColMargin] = Trio(ru: "Маржа", uk: "Маржа", en: "Margin"),
        [BotKeys.ScenarioColResult] = Trio(ru: "UAH", uk: "UAH", en: "UAH"),
        [BotKeys.ScenarioColProfit] = Trio(ru: "Прибыль ₴/€", uk: "Прибуток ₴/€", en: "Profit ₴/€"),

        [BotKeys.KbRates] = Trio(ru: "💱 Курсы", uk: "💱 Курси", en: "💱 Rates"),
        [BotKeys.KbSettings] = Trio(ru: "⚙️ Настройки", uk: "⚙️ Налаштування", en: "⚙️ Settings"),
        [BotKeys.KbHelp] = Trio(ru: "❓ Помощь", uk: "❓ Допомога", en: "❓ Help"),
        [BotKeys.KbBack] = Trio(ru: "← Назад", uk: "← Назад", en: "← Back"),
        [BotKeys.KbCustom] = Trio(ru: "✏️ Своё", uk: "✏️ Своє", en: "✏️ Custom"),
        [BotKeys.KbResetAll] = Trio(ru: "♻️ Сбросить всё", uk: "♻️ Скинути все", en: "♻️ Reset all"),
        [BotKeys.KbRefresh] = Trio(ru: "🔄 Обновить", uk: "🔄 Оновити", en: "🔄 Refresh"),
        [BotKeys.KbDefault] = Trio(ru: "⤺ По умолчанию", uk: "⤺ За замовчуванням", en: "⤺ Default"),
        [BotKeys.KbDefaultMargin] = Trio(ru: "⤺ Моя маржа", uk: "⤺ Моя маржа", en: "⤺ My margin"),
        [BotKeys.KbDefaultBank] = Trio(ru: "⤺ Мой банк", uk: "⤺ Мій банк", en: "⤺ My bank"),
        [BotKeys.KbHistoryPrev] = Trio(ru: "← Предыдущие", uk: "← Попередні", en: "← Previous"),
        [BotKeys.KbHistoryNext] = Trio(ru: "Следующие →", uk: "Наступні →", en: "Next →"),
        [BotKeys.KbHistoryRecordingOn] = Trio(ru: "📥 Запись: вкл", uk: "📥 Запис: увімк.", en: "📥 Recording: on"),
        [BotKeys.KbHistoryRecordingOff] = Trio(ru: "📥 Запись: выкл", uk: "📥 Запис: вимк.", en: "📥 Recording: off"),
        [BotKeys.KbClearHistory] = Trio(ru: "🗑️ Очистить всё", uk: "🗑️ Очистити все", en: "🗑️ Clear all"),
        [BotKeys.KbShowHistory] = Trio(ru: "👁 Показать", uk: "👁 Показати", en: "👁 Show"),
        [BotKeys.KbHistoryStats] = Trio(ru: "📊 Статистика", uk: "📊 Статистика", en: "📊 Stats"),
        [BotKeys.KbConvertMark] = Trio(ru: "⏳ Отметить как сделку", uk: "⏳ Позначити як угоду", en: "⏳ Mark as trade"),
        [BotKeys.KbConvertUnmark] = Trio(ru: "✅ Сделка ✓ (снять)", uk: "✅ Угода ✓ (зняти)", en: "✅ Trade ✓ (unmark)"),
        [BotKeys.KbConvertTune] = Trio(ru: "🔧 Параметры", uk: "🔧 Параметри", en: "🔧 Adjust"),
        [BotKeys.KbConvertCollapse] = Trio(ru: "↩ Свернуть", uk: "↩ Згорнути", en: "↩ Collapse"),
        [BotKeys.KbStatsToday] = Trio(ru: "Сегодня", uk: "Сьогодні", en: "Today"),
        [BotKeys.KbStatsWeek] = Trio(ru: "7 дней", uk: "7 днів", en: "7 days"),
        [BotKeys.KbStatsMonth] = Trio(ru: "30 дней", uk: "30 днів", en: "30 days"),
        [BotKeys.KbStatsAll] = Trio(ru: "Всё время", uk: "Весь час", en: "All time"),

        [BotKeys.CbAck] = Trio(ru: "✓", uk: "✓", en: "✓"),
        [BotKeys.CbRefreshed] = Trio(ru: "🔄 Обновлено", uk: "🔄 Оновлено", en: "🔄 Refreshed"),
        [BotKeys.CbHistoryCleared] = Trio(ru: "🗑️ Очищено", uk: "🗑️ Очищено", en: "🗑️ Cleared"),
        [BotKeys.CbHistoryRecordingOn] = Trio(ru: "📥 Запись включена", uk: "📥 Запис увімкнено", en: "📥 Recording on"),
        [BotKeys.CbHistoryRecordingOff] = Trio(ru: "📥 Запись выключена", uk: "📥 Запис вимкнено", en: "📥 Recording off"),
        [BotKeys.CbDoneOn] = Trio(ru: "🟢 Помечено как сделка", uk: "🟢 Позначено як угоду", en: "🟢 Marked as trade"),
        [BotKeys.CbDoneOff] = Trio(ru: "⏳ Возвращено в черновик", uk: "⏳ Повернуто в чернетку", en: "⏳ Reverted to draft"),
        [BotKeys.CbNotFound] = Trio(ru: "Запись не найдена", uk: "Запис не знайдено", en: "Entry not found"),
        [BotKeys.CbMarked] = Trio(ru: "🟢 Отмечено как сделка", uk: "🟢 Позначено як угоду", en: "🟢 Marked"),
        [BotKeys.CbUnmarked] = Trio(ru: "↩ Снято с сделки", uk: "↩ Знято з угоди", en: "↩ Unmarked"),
        [BotKeys.CbBestHintOn] = Trio(ru: "🏆 Подсказка включена", uk: "🏆 Підказку увімкнено", en: "🏆 Hint enabled"),
        [BotKeys.CbBestHintOff] = Trio(ru: "🏆 Подсказка отключена", uk: "🏆 Підказку вимкнено", en: "🏆 Hint disabled"),

        [BotKeys.HistoryStatusOn] = Trio(ru: "вкл", uk: "увімк.", en: "on"),
        [BotKeys.HistoryStatusOff] = Trio(ru: "выкл", uk: "вимк.", en: "off"),

        [BotKeys.SettingsCurrent] = Trio(
            ru: "⚙️ *Настройки*\n\n🌐 Язык: {0}\n📐 Маржа: {1}\n🏦 Банк: {2}\n💶 Валюта: {3}\n💰 Сумма: {4}",
            uk: "⚙️ *Налаштування*\n\n🌐 Мова: {0}\n📐 Маржа: {1}\n🏦 Банк: {2}\n💶 Валюта: {3}\n💰 Сума: {4}",
            en: "⚙️ *Settings*\n\n🌐 Language: {0}\n📐 Margin: {1}\n🏦 Bank: {2}\n💶 Currency: {3}\n💰 Amount: {4}"),

        [BotKeys.SettingsValueDefault] = Trio(
            ru: "{0} _(по умолчанию)_",
            uk: "{0} _(за замовчуванням)_",
            en: "{0} _(default)_"),

        [BotKeys.SettingsResetAll] = Trio(
            ru: "Все настройки сброшены.",
            uk: "Усі налаштування скинуто.",
            en: "All settings reset."),

        [BotKeys.SettingsUseMenu] = Trio(
            ru: "Изменения настроек — только через меню. Жми /settings.",
            uk: "Зміни налаштувань — лише через меню. Тисни /settings.",
            en: "Settings can only be changed via the menu. Tap /settings."),

        [BotKeys.SettingsUserUnknown] = Trio(
            ru: "Не смог определить пользователя.",
            uk: "Не вдалося визначити користувача.",
            en: "Couldn't identify user."),

        [BotKeys.SettingsTitleLang] = Trio(ru: "🌐 *Язык*\nТекущий: {0}", uk: "🌐 *Мова*\nПоточна: {0}", en: "🌐 *Language*\nCurrent: {0}"),
        [BotKeys.SettingsTitleBank] = Trio(ru: "🏦 *Банк по умолчанию*\nТекущий: {0}", uk: "🏦 *Банк за замовчуванням*\nПоточний: {0}", en: "🏦 *Default bank*\nCurrent: {0}"),
        [BotKeys.SettingsTitleCurrency] = Trio(ru: "💶 *Валюта по умолчанию*\nТекущая: {0}", uk: "💶 *Валюта за замовчуванням*\nПоточна: {0}", en: "💶 *Default currency*\nCurrent: {0}"),
        [BotKeys.SettingsTitleMargin] = Trio(ru: "📐 *Маржа*\nТекущая: {0}", uk: "📐 *Маржа*\nПоточна: {0}", en: "📐 *Margin*\nCurrent: {0}"),
        [BotKeys.SettingsTitleAmount] = Trio(ru: "💰 *Сумма по умолчанию*\nТекущая: {0}", uk: "💰 *Сума за замовчуванням*\nПоточна: {0}", en: "💰 *Default amount*\nCurrent: {0}"),

        [BotKeys.SettingsBtnLang] = Trio(ru: "🌐 Язык", uk: "🌐 Мова", en: "🌐 Language"),
        [BotKeys.SettingsBtnMargin] = Trio(ru: "📐 Маржа", uk: "📐 Маржа", en: "📐 Margin"),
        [BotKeys.SettingsBtnBank] = Trio(ru: "🏦 Банк", uk: "🏦 Банк", en: "🏦 Bank"),
        [BotKeys.SettingsBtnCurrency] = Trio(ru: "💶 Валюта", uk: "💶 Валюта", en: "💶 Currency"),
        [BotKeys.SettingsBtnAmount] = Trio(ru: "💰 Сумма", uk: "💰 Сума", en: "💰 Amount"),
        [BotKeys.SettingsBtnHistory] = Trio(ru: "📜 История", uk: "📜 Історія", en: "📜 History"),
        [BotKeys.SettingsBtnBestHintOn] = Trio(ru: "🏆 Подсказка лучшего: вкл", uk: "🏆 Підказка найкращого: увімк.", en: "🏆 Best hint: on"),
        [BotKeys.SettingsBtnBestHintOff] = Trio(ru: "🏆 Подсказка лучшего: выкл", uk: "🏆 Підказка найкращого: вимк.", en: "🏆 Best hint: off"),

        [BotKeys.SettingsTitleHistory] = Trio(
            ru: "📜 *История*\nВсего записей: {0}\nЗапись новых: {1}",
            uk: "📜 *Історія*\nУсього записів: {0}\nЗапис нових: {1}",
            en: "📜 *History*\nTotal entries: {0}\nRecording: {1}"),

        [BotKeys.SettingsPromptMargin] = Trio(
            ru: "📐 Введи маржу в процентах (0..50). Например: `8.5`",
            uk: "📐 Введи маржу у відсотках (0..50). Наприклад: `8.5`",
            en: "📐 Enter margin in percent (0..50). For example: `8.5`"),

        [BotKeys.SettingsPromptAmount] = Trio(
            ru: "💰 Введи сумму больше 0. Например: `250`",
            uk: "💰 Введи суму більше 0. Наприклад: `250`",
            en: "💰 Enter amount above 0. For example: `250`"),

        [BotKeys.SettingsCustomApplied] = Trio(ru: "✅ Сохранено: *{0}*", uk: "✅ Збережено: *{0}*", en: "✅ Saved: *{0}*"),
        [BotKeys.SettingsCustomInvalid] = Trio(ru: "❌ Не подошло: `{0}`", uk: "❌ Не підійшло: `{0}`", en: "❌ Invalid: `{0}`"),

        [BotKeys.HistoryEmpty] = Trio(
            ru: "История пуста.",
            uk: "Історія порожня.",
            en: "History is empty."),

        [BotKeys.HistoryPageHeader] = Trio(
            ru: "📜 *Записи {0}..{1} из {2}*",
            uk: "📜 *Записи {0}..{1} з {2}*",
            en: "📜 *Entries {0}..{1} of {2}*"),

        [BotKeys.HistoryUserUnknown] = Trio(
            ru: "Не смог определить пользователя.",
            uk: "Не вдалося визначити користувача.",
            en: "Couldn't identify user."),

        [BotKeys.HistoryStatsHeader] = Trio(
            ru: "📊 *Статистика*",
            uk: "📊 *Статистика*",
            en: "📊 *Statistics*"),

        [BotKeys.HistoryStatsCounts] = Trio(
            ru: "📋 Всего операций: *{0}*\n🟢 Из них отмечено: *{1}*  ⏳ Черновиков: *{2}*",
            uk: "📋 Усього операцій: *{0}*\n🟢 З них позначено: *{1}*  ⏳ Чернеток: *{2}*",
            en: "📋 Total operations: *{0}*\n🟢 Marked: *{1}*  ⏳ Drafts: *{2}*"),

        [BotKeys.HistoryStatsPeriod] = Trio(
            ru: "📅 Период: {0} — {1}",
            uk: "📅 Період: {0} — {1}",
            en: "📅 Period: {0} — {1}"),

        [BotKeys.HistoryStatsVolumeTitle] = Trio(
            ru: "*Объём по валютам*",
            uk: "*Обсяг за валютами*",
            en: "*Volume by currency*"),

        [BotKeys.HistoryStatsVolumeRow] = Trio(
            ru: "{0} {1}: *{2}* {3}",
            uk: "{0} {1}: *{2}* {3}",
            en: "{0} {1}: *{2}* {3}"),

        [BotKeys.HistoryStatsUahTitle] = Trio(
            ru: "💰 Оборот UAH: *{0} ₴*",
            uk: "💰 Оборот UAH: *{0} ₴*",
            en: "💰 UAH turnover: *{0} ₴*"),

        [BotKeys.HistoryStatsProfit] = Trio(
            ru: "📈 Прибыль (расчётная): *{0} ₴*{1}",
            uk: "📈 Прибуток (розрахунковий): *{0} ₴*{1}",
            en: "📈 Profit (calculated): *{0} ₴*{1}"),

        [BotKeys.HistoryStatsAvgMargin] = Trio(
            ru: "📐 Средняя маржа: *{0}%*",
            uk: "📐 Середня маржа: *{0}%*",
            en: "📐 Average margin: *{0}%*"),

        [BotKeys.HistoryStatsTopBank] = Trio(
            ru: "🏦 Топ-банк: `{0}` ({1})",
            uk: "🏦 Топ-банк: `{0}` ({1})",
            en: "🏦 Top bank: `{0}` ({1})"),

        [BotKeys.HistoryStatsTopCurrency] = Trio(
            ru: "💎 Топ-валюта: *{0}*",
            uk: "💎 Топ-валюта: *{0}*",
            en: "💎 Top currency: *{0}*"),

        [BotKeys.HistoryStatsMaxTrade] = Trio(
            ru: "🥇 Самая крупная сделка: *{0} {1}* → *{2} ₴*",
            uk: "🥇 Найбільша угода: *{0} {1}* → *{2} ₴*",
            en: "🥇 Biggest trade: *{0} {1}* → *{2} ₴*"),

        [BotKeys.HistoryStatsEmpty] = Trio(
            ru: "История пуста — статистика появится после первого расчёта.",
            uk: "Історія порожня — статистика з'явиться після першого розрахунку.",
            en: "History is empty — stats will appear after your first calculation."),

        [BotKeys.HistoryStatsPeriodLabel] = Trio(
            ru: "*Период:* {0}",
            uk: "*Період:* {0}",
            en: "*Period:* {0}"),
    };

    private static Dictionary<string, string> Trio(string ru, string uk, string en) => new()
    {
        ["ru"] = ru,
        ["uk"] = uk,
        ["en"] = en,
    };
}
