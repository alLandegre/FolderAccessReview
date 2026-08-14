# Сборка установщика Folder Access Review

## Требования
- .NET 8 SDK
- Inno Setup 6 (`ISCC.exe`)

## Собрать Setup.exe

Из корня репозитория:

```powershell
.\setup\build-installer.ps1
```

Результат: `installer\FolderAccessReview-Setup-1.5.0.exe`

Установка — **только для текущего пользователя** (`%LocalAppData%\Programs\Folder Access Review`), ярлык на рабочем столе: **Folder Access Review**.
