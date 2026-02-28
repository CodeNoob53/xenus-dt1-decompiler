# Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)

**[English version (README.md)](README.md)**

Інструмент для масового декодування стиснутого кешу текстур гри **Xenus 2: White Gold** (файли `*.DT1` / `*.DT2`) у стандартні формати зображень (`.dds`, `.bmp`, `.tga` тощо) зі збереженням оригінальної ієрархії папок.

---

## Чому саме такий підхід

Рушій гри (**Vital Engine 3**) використовує модифікований zlib із власними пресет-словниками. Відтворити декомпресор з нуля практично неможливо, оскільки словник вбудований глибоко всередині рушія. Замість цього утиліта завантажує оригінальну `VELoader.dll` з гри та викликає її API через Windows Native Interop:

| Експорт | Призначення |
|---|---|
| `GetCLVersion` | Перевірка версії / сумісності |
| `GetUnloadSize` | Отримати точний розмір розпакованих даних |
| `Unload` | Розпакувати payload у буфер |

Завдяки цьому декомпресія побітово ідентична оригінальній і не потребує знання внутрішнього словника.

---

## Формат файлів DT1 / DT2

Кожен файл `.DT1` або `.DT2` — це один стиснутий ресурс. 8-байтний заголовок, за яким одразу йде стиснутий payload:

```
Зміщення  Розмір  Опис
0          3       Розмір розпакованих даних, little-endian 24-bit
3          1       Прапорці (0x50 у всіх спостережуваних файлах)
4          3       Розмір стиснутого payload, little-endian 24-bit
7          1       Прапорці (0x08 у всіх спостережуваних файлах)
8          N       Стиснутий payload (VE3 zlib, починається з 0x16 0x30)
```

Формат виводу визначається з оригінальної назви файлу. Файли слідують конвенції `<base>_<EXT>.DT1`, де `EXT` — реальний формат (наприклад, `GRASS_TGA.DT1` → `GRASS.tga`, `GROUP_4_0_BMP.DT1` → `GROUP_4_0.bmp`). Формат можна перевизначити 4-м аргументом командного рядка.

> **Важливо:** Цей інструмент обробляє лише файли `.DT1` / `.DT2`. Архіви у форматі `CE#$` (наприклад, `CACHE/*.DAT`) мають іншу структуру — формат повністю задокументований і підтримка `CE#$` може бути інтегрована у майбутніх версіях утиліти після фінального доопрацювання.

---

## Вимоги

- **ОС:** Windows (хост x86 або x64)
- **Runtime:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Архітектура:** публікація обов'язково як **win-x86** — `VELoader.dll` є 32-бітною DLL
- **VELoader.dll:** з кореневої папки гри або з розпакованого `.grp`-архіву

---

## Структура проєкту

```
xenus-dt1-decompiler/
├── src/
│   └── XenusDt1Decompiler/
│       ├── XenusDt1Decompiler.csproj
│       └── Program.cs
├── .github/
│   └── workflows/
│       └── release.yml
├── run_decompiler.bat
├── .gitignore
├── README.md
└── README_UA.md
```

---

## Збірка

Стандартна debug/release збірка:

```powershell
dotnet build .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release
```

Self-contained публікація в один exe (рекомендовано для поширення):

```powershell
dotnet publish .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release -r win-x86 --self-contained true
```

Виконуваний файл буде в `bin\Release\net8.0\win-x86\publish\`.

---

## Автоматичний реліз (GitHub Actions)

Workflow за адресою `.github/workflows/release.yml` запускається при пуші тегу версії. Він збирає проєкт і створює GitHub Release з архівом `win-x86.zip` (exe + `run_decompiler.bat`):

```powershell
git tag v1.0.0
git push origin v1.0.0
```

---

## Використання

```
xenus-dt1-decompiler.exe <вхід> [вихідна_папка] [шлях_до_veloader] [формат]
```

| Аргумент | За замовчуванням | Опис |
|---|---|---|
| `вхід` | — | Шлях до файлу `.DT1`/`.DT2` **або** до папки для рекурсивного сканування |
| `вихідна_папка` | поряд із входом | Папка для збереження декодованих файлів |
| `шлях_до_veloader` | автопошук | Явний шлях до `VELoader.dll` |
| `формат` | із назви файлу | Примусове розширення виводу: `dds`, `bmp`, `tga` тощо |

Якщо `шлях_до_veloader` не вказано, утиліта шукає `VELoader.dll` у поточному каталозі, `.\GrpUnpacker\` та `..\`.

### Приклади

**Декодувати один файл текстури:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES\GRASS_TGA.DT1" ".\out_tex"
```

**Масове декодування всіх текстур з автопошуком VELoader:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex"
```

**Масове декодування з примусовим форматом DDS:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex" "C:\Games\Xenus 2\VELoader.dll" dds
```

Ієрархія підпапок відносно `вхід` відтворюється у `вихідна_папка`.

### Виведення в консолі

```
[OK]   path\to\file.DT1 -> out\file.bmp  (49208 bytes, sig='BM..', ver=0x200, hdrUnc=49200, apiUnc=49200)
[FAIL] path\to\broken.DT1 : Unload returned 0 (...)
Done. OK=308, FAIL=2
```

---

## Коди виходу

| Код | Значення |
|---|---|
| 0 | Всі файли успішно декодовано |
| 1 | Один або більше файлів завершились помилкою |
| 2 | Не вказано аргументів |
| 3 | `VELoader.dll` не знайдено |
| 4 | У вхідній папці не знайдено DT1/DT2 файлів |
| 5 | Вхідний шлях не існує |

---

## Контриб'юторам

- **Не** додавайте ігрові файли (`.DT1`, `.DT2`, `.dds`, `.bmp`, `.png`, `VELoader.dll`).
- У репозиторій належать лише вихідний код, документація та скрипти збірки.
- Деталі — у файлі [CONTRIBUTING.md](CONTRIBUTING.md).
- Ліцензія: **MIT** — див. [LICENSE](LICENSE).

---

## Відмова від відповідальності

Цей репозиторій створено виключно для дослідницьких цілей та забезпечення сумісності. Користувач несе повну відповідальність за дотримання місцевого законодавства та ліцензійної угоди (EULA) гри Xenus 2: White Gold при роботі з її файлами.
