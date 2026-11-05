# MobiStore

برنامج ويندوز عربي لإدارة محل إكسسوارات الموبايل، مع خدمة ASP.NET Core وقاعدة SQL Server Express.

**الحالة:** نسخة تطوير 0.2 للتجربة، وليست إصدارًا مكتملًا أو معتمدًا للإنتاج.

- [دليل التشغيل بالعربية](README.ar.md)
- [إضافات الإصدار 0.2 وحدوده](docs/RELEASE-0.2.ar.md)
- [خطة العمل](docs/ROADMAP.ar.md)

## Requirements

Windows, .NET 10 SDK, SQL Server Express, PowerShell, and Git.

```powershell
.\scripts\build.ps1
.\scripts\start.ps1
```

Configure your SQL Server instance through `src/MobiStore.Server/appsettings.Local.json` (excluded from Git). No default administrator password is supplied. Create the initial administrator through application setup.

The repository contains application source and schema definitions. It excludes live databases, backups, local configuration, credentials, runtime artifacts, and reference-program recordings.
