' Запускает run-bot.bat в фоне без окна консоли.
' Использование: дабл-клик по этому файлу или ярлык в shell:startup.

Set WshShell = CreateObject("WScript.Shell")
strPath = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
WshShell.Run Chr(34) & strPath & "\run-bot.bat" & Chr(34), 0, False
