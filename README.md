# KeySaver

Настольное приложение на C# и WPF. Целевая платформа — Windows 7 SP1 x64 с .NET Framework 4.8. KeySaver открывает локальную историю сессий и поддерживает импорт и экспорт JSON и архивов `.keysaver`.

## Сборка

На машине разработчика нужен .NET Framework 4.8 Developer Pack. Соберите проект на Windows:

```powershell
dotnet build desktop/KeySaver.csproj -c Release
```

Исполняемый файл находится в `desktop/bin/Release/net48/KeySaver.exe`. Данные настольной версии хранятся в `%LocalAppData%\KeySaver`. Веб-макет открывается через `index.html`.

