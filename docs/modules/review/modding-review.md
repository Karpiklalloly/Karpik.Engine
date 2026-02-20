# Code Review: Modding (Core + Lua)

**Module:** Modding.Core + Modding.Lua  
**Type:** Scripting/Modding System  
**Status:** ⚠️ Needs Work - Performance & Safety Issues

---

## Overview

Система модов на базе MoonSharp (Lua). Позволяет загружать, инициализировать и обновлять Lua-скрипты как моды. Поддерживает hot-reload и изоляцию модов.

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 10 |
| Interfaces | 2 |
| Classes | 6 |
| Lines of Code | ~500 |

---

## Issues Found

### CR-1: Singleton Logger без возможности замены [Critical]

**File:** [`ModContainer.cs`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:183)

```csharp
private async JobHandle Log(string message, LogLevel level = LogLevel.Debug)
{
    await Logger.Instance.Log($"[Mod: {MetaData.Name}] {message}", level);
}
```

**Problem:** Прямое использование `Logger.Instance` вместо DI. Нарушает тестируемость и гибкость.

**Solution:** Инжектировать через DI:
```csharp
[DI] private ILogger _logger = null!;

private async JobHandle Log(string message, LogLevel level = LogLevel.Debug)
{
    await _logger.Log($"[Mod: {MetaData.Name}] {message}", level);
}
```

---

### CR-2: GetAwaiter().GetResult() — Potential Deadlock [Critical]

**File:** [`ModContainer.cs`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:64)

```csharp
public DynValue LoadModule(string moduleName)
{
    try
    {
        // ...
    }
    catch (Exception ex)
    {
        Log($"Error loading module {moduleName}: {ex}", LogLevel.Error).GetAwaiter().GetResult();
        return DynValue.Nil;
    }
}
```

**Problem:** `GetAwaiter().GetResult()` может вызвать deadlock в синхронном контексте. Также используется в [`Update()`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:79), [`Start()`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:94), [`Load()`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:109).

**Solution:** Сделать методы async или использовать синхронный логгинг:
```csharp
// Вариант 1: Синхронный лог для ошибок
private void LogSync(string message, LogLevel level = LogLevel.Debug)
{
    Logger.Instance.LogSync($"[Mod: {MetaData.Name}] {message}", level);
}

// Вариант 2: Fire-and-forget с обработкой ошибок
private void Log(string message, LogLevel level = LogLevel.Debug)
{
    _ = LogAsync(message, level);
}
```

---

### HI-1: ConcurrentDictionary без необходимости [High]

**File:** [`ModManager.cs`](Modules/Shared/Modding/Modding.Lua/ModManager.cs:12)

```csharp
private readonly ConcurrentDictionary<string, ModContainer> _loadedMods = new();
```

**Problem:** ConcurrentDictionary используется, но все операции (LoadMods, UpdateMods, StartMods) выполняются синхронно в одном потоке. Overhead без пользы.

**Solution:** Использовать обычный Dictionary если нет реальной многопоточности:
```csharp
private readonly Dictionary<string, ModContainer> _loadedMods = new();
```

---

### HI-2: ToArray() перед итерацией — лишняя аллокация [High]

**File:** [`ModManager.cs`](Modules/Shared/Modding/Modding.Lua/ModManager.cs:43)

```csharp
await foreach (var modDir in FileSystem.GetDirectories(modsRootDirectory).ToArray().ToAsyncEnumerable())
```

**Problem:** `ToArray()` создаёт копию коллекции перед итерацией. Лишняя аллокация.

**Solution:** Убрать ToArray() если не требуется модификация коллекции во время итерации:
```csharp
await foreach (var modDir in FileSystem.GetDirectories(modsRootDirectory).ToAsyncEnumerable())
```

---

### HI-3: Отсутствует sandbox для Lua-скриптов [High]

**File:** [`ModContainer.cs`](Modules/Shared/Modding/Modding.Lua/ModContainer.cs:36)

```csharp
internal ModContainer(string directoryPath, AssetHandle<ModMetaDataAsset> metaDataHandle)
{
    Script = new Script();
    Script.Options.ScriptLoader = new ModScriptLoader(directoryPath, this);
    Script.Options.DebugPrint = s => Log(s);
}
```

**Problem:** Lua-скрипты имеют полный доступ к .NET через MoonSharp. Моды могут выполнять любой код.

**Solution:** Ограничить доступ:
```csharp
Script = new Script(CoreModules.Preset_SoftSandbox);
// Или явно запретить опасные типы
Script.Globals["io"] = DynValue.Nil;
Script.Globals["os"] = DynValue.Nil;
```

---

### HI-4: DI поле не инициализировано [High]

**File:** [`ModManager.cs`](Modules/Shared/Modding/Modding.Lua/ModManager.cs:13-14)

```csharp
[DI] private IAssetsManager _assetsManager;
[DI] private IServiceContainer _serviceProvider;
```

**Problem:** Поля без `= null!` — компилятор выдаст предупреждение с NRT.

**Solution:**
```csharp
[DI] private IAssetsManager _assetsManager = null!;
[DI] private IServiceContainer _serviceProvider = null!;
```

---

### MI-1: Пустой ModdingInstaller [Medium]

**File:** [`ModdingInstaller.cs`](Modules/Shared/Modding/Modding.Core/ModdingInstaller.cs:7)

```csharp
[Module]
public class ModdingInstaller : IModule, IModuleConfiguratable
{
    public void OnRegisterServices(IServiceRegister services)
    {
        // Пусто
    }
}
```

**Problem:** Core-модуль не регистрирует IModManager. Регистрация происходит в Modding.Lua.

**Solution:** Либо перенести регистрацию сюда, либо документировать намеренность.

---

### MI-2: Exception swallowing [Medium]

**File:** [`ModManager.cs`](Modules/Shared/Modding/Modding.Lua/ModManager.cs:139)

```csharp
public void ExecuteForMod(string modId, Action<Script> action)
{
    try
    {
        action(container.Script);
    }
    catch (Exception ex)
    {
        Logger.Instance.Log(...); // Exception проглочен, не переброшен
    }
}
```

**Problem:** Исключения в модах проглатываются. Сложно отлаживать.

**Solution:** Добавить опцию для переброса или детальное логирование:
```csharp
catch (Exception ex)
{
    Logger.Instance.Log(..., LogLevel.Error);
    #if DEBUG
    throw; // В DEBUG режиме перебрасываем
    #endif
}
```

---

## Positive Aspects ✅

1. **Хорошая архитектура** — разделение Core (интерфейсы) и Lua (реализация)
2. **ModMetaData как readonly struct** — immutable данные мода
3. **GameAPI для Lua** — контролируемый API для модов
4. **Поддержка lifecycle** — Load → Start → Update → Unload

---

## Recommendations

| Priority | Action |
|----------|--------|
| **Critical** | Убрать GetAwaiter().GetResult() — использовать sync логгинг |
| **Critical** | Инжектировать Logger через DI |
| **High** | Добавить sandbox для Lua-скриптов |
| **High** | Убрать ToArray() в итерациях |
| **High** | Добавить `= null!` для DI-полей |
| Medium | Заменить ConcurrentDictionary на Dictionary |
| Medium | Добавить опцию для переброса исключений в DEBUG |

---

## Verdict

**Хорошая архитектура, но есть критичные проблемы.** `GetAwaiter().GetResult()` — потенциальный deadlock. Отсутствие sandbox — проблема безопасности. Требуется рефакторинг логгирования.
