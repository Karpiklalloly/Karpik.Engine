# AssetManagement - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Race Condition при загрузке ассетов
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:122-168)

```csharp
private async JobHandle<Asset> LoadAssetInternal(string path, Type assetType)
{
    int id = AssetPath.GetHash(path);
    var cacheKey = (id, assetType);
    
    if (_loadedAssets.TryGetValue(cacheKey, out var existingAsset))
    {
        return existingAsset;
    }
    // ... загрузка ...
    _loadedAssets.TryAdd(cacheKey, newAsset);
}
```

**Проблема**: Между `TryGetValue` и `TryAdd` есть окно для race condition. Если два потока одновременно запросят один и тот же ассет, он будет загружен дважды.

**Решение**:
```csharp
private readonly ConcurrentDictionary<(int, Type), TaskCompletionSource<Asset>> _loadingAssets = new();

private async JobHandle<Asset> LoadAssetInternal(string path, Type assetType)
{
    int id = AssetPath.GetHash(path);
    var cacheKey = (id, assetType);
    
    // Проверяем кеш
    if (_loadedAssets.TryGetValue(cacheKey, out var existingAsset))
        return existingAsset;
    
    // Пытаемся начать загрузку
    var tcs = new TaskCompletionSource<Asset>(TaskCreationOptions.RunContinuationsAsynchronously);
    
    if (_loadingAssets.TryAdd(cacheKey, tcs))
    {
        // Мы первые - загружаем
        try
        {
            var asset = await LoadAssetCore(path, assetType);
            _loadedAssets.TryAdd(cacheKey, asset);
            tcs.SetResult(asset);
            return asset;
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _loadingAssets.TryRemove(cacheKey, out _);
        }
    }
    else
    {
        // Кто-то уже загружает - ждём
        return await tcs.Task;
    }
}
```

---

### CR-002: Null-forgiving operator без проверки
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:52)

```csharp
var loaderInstance = (IAssetSaver)Activator.CreateInstance(loaderType)!;
```

**Проблема**: `Activator.CreateInstance` может вернуть `null` для nullable типов или при ошибке. Оператор `!` скрывает потенциальную проблему.

**Решение**:
```csharp
var loaderInstance = Activator.CreateInstance(loaderType) as IAssetSaver 
    ?? throw new InvalidOperationException($"Failed to create instance of {loaderType.Name}");
```

---

## 🟡 Высокий приоритет

### HI-001: LINQ в рефлексии
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:44-46)

```csharp
var loaderTypes = assembly.GetTypes()
    .Where(type => typeof(IAssetSaver).IsAssignableFrom(type)
                   && type is { IsInterface: false, IsAbstract: false, IsGenericType: false });
```

**Проблема**: LINQ создаёт аллокации при каждом вызове. Методы `RegisterLoaders/RegisterSavers` вызываются при инициализации, но всё же создают мусор.

**Решение**:
```csharp
foreach (var type in assembly.GetTypes())
{
    if (type.IsInterface || type.IsAbstract || type.IsGenericType) continue;
    if (!typeof(IAssetSaver).IsAssignableFrom(type)) continue;
    // ...
}
```

---

### HI-002: Отсутствует проверка на null для _serviceProvider
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:84)

```csharp
private void RegisterLoaderInternal(IAssetLoader loader)
{
    _serviceProvider.Inject(loader);
    // ...
}
```

**Проблема**: Если `_serviceProvider` не инициализирован через DI, будет `NullReferenceException`.

**Решение**:
```csharp
private void RegisterLoaderInternal(IAssetLoader loader)
{
    _serviceProvider?.Inject(loader);
    // ...
}
```

---

### HI-003: AssetHandle можно скопировать после Dispose
**Файл**: [`AssetHandle.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetHandle.cs:19-27)

```csharp
public void Dispose()
{
    if (Asset is not null && _manager is not null)
    {
        _manager.ReleaseAsset(Asset);
        Asset = null;
        _manager = null;
    }
}
```

**Проблема**: `AssetHandle` — это `struct`. При копировании создаётся новая копия, но `RefCount` увеличивается только один раз. При `Dispose` обеих копий `RefCount` уменьшится дважды.

**Решение**:
```csharp
public struct AssetHandle<T> : IDisposable where T : Asset
{
    private T? _asset;
    private IAssetsManager? _manager;
    private bool _disposed;

    internal AssetHandle(T asset, IAssetsManager manager)
    {
        _asset = asset;
        _manager = manager;
        _disposed = false;
        _asset.IncrementRef();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (_asset is not null && _manager is not null)
        {
            _manager.ReleaseAsset(_asset);
            _asset = null;
            _manager = null;
        }
    }
}
```

---

### HI-004: Отсутствует обработка исключений при загрузке
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:157-167)

```csharp
await using Stream stream = _fileSystem.OpenRead(targetPath);
var newAsset = await loader.LoadAsync(stream, targetPath);
```

**Проблема**: Если `LoadAsync` выбросит исключение, ассет останется в `_loadedAssets` с некорректным состоянием.

**Решение**:
```csharp
Asset newAsset = null!;
try
{
    await using Stream stream = _fileSystem.OpenRead(targetPath);
    newAsset = await loader.LoadAsync(stream, targetPath);
    // ... инициализация ...
    _loadedAssets.TryAdd(cacheKey, newAsset);
    newAsset.Load();
}
catch
{
    // Удаляем из кеша если успели добавить
    _loadedAssets.TryRemove(cacheKey, out _);
    throw;
}
```

---

## 🟢 Средний приоритет

### MI-001: Дублирование логики поиска пути
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:140-155)

```csharp
if (!_fileSystem.Exists(targetPath))
{
    var modsSubPath = _fileSystem.Combine(ModsPath, targetPath);
    targetPath = _fileSystem.Exists(modsSubPath)
        ? modsSubPath
        : _fileSystem.Combine(ContentPath, targetPath);
}
```

**Проблема**: Эта логика дублируется в `LoadAssetInternal` и `SaveAssetAsync`.

**Решение**:
```csharp
private string ResolvePath(string path)
{
    if (_fileSystem.Exists(path)) return path;
    
    var modsPath = _fileSystem.Combine(ModsPath, path);
    if (_fileSystem.Exists(modsPath)) return modsPath;
    
    return _fileSystem.Combine(ContentPath, path);
}
```

---

### MI-002: Неэффективный поиск по расширению
**Файл**: [`AssetsManager.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs:108)

```csharp
foreach (var pair in _loaders.Where(x => x.Key.Item1 == ext))
```

**Проблема**: LINQ `Where` создаёт аллокации. Для словаря это неэффективный поиск.

**Решение**: Использовать отдельный словарь для поиска по расширению:
```csharp
// [Extension] -> [List of (Asset Type, Loader)]
private readonly Dictionary<string, List<(Type, IAssetLoader)>> _loadersByExtension = new();
```

---

### MI-003: Asset.Type избыточен
**Файл**: [`Asset.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/Asset.cs:6)

```csharp
public Type Type { get; internal set; }
```

**Проблема**: `Type` всегда равен `GetType()`. Это дублирование.

**Решение**:
```csharp
public Type Type => GetType(); // Вычисляемое свойство
```

---

### MI-004: Рекурсивная проверка зависимостей
**Файл**: [`Asset.cs`](Modules/Shared/AssetManagement/AssetManagement.Core/Asset.cs:36-46)

```csharp
bool ChildHasDependencyOnParent(Asset child)
{
    if (child._dependencies.Contains(this)) return true;
    foreach (var dep in child._dependencies)
    {
        if (ChildHasDependencyOnParent(dep)) return true;
    }
    return false;
}
```

**Проблема**: Рекурсивная проверка может вызвать `StackOverflowException` при глубокой иерархии зависимостей.

**Решение**: Использовать итеративный подход с `HashSet`:
```csharp
private bool HasCyclicDependency(Asset child)
{
    var visited = new HashSet<Asset>();
    var stack = new Stack<Asset>();
    stack.Push(child);
    
    while (stack.Count > 0)
    {
        var current = stack.Pop();
        if (current == this) return true;
        if (!visited.Add(current)) continue;
        
        foreach (var dep in current._dependencies)
            stack.Push(dep);
    }
    return false;
}
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | ~10 |
| Классов | ~8 |
| Строк кода | ~250 |
| Критичных проблем | 2 |
| Высокий приоритет | 4 |
| Средний приоритет | 4 |

---

## 💡 Предложения по развитию

### 1. Async Loading Queue
Добавить очередь загрузки с приоритетами:
```csharp
public enum LoadPriority
{
    Critical,  // UI, необходимые для старта
    High,      // Игровые объекты
    Normal,    // Текстуры, модели
    Low        // Фоновая загрузка
}
```

### 2. Asset Bundles
Добавить поддержку пакетов ассетов для уменьшения количества файлов.

### 3. Hot Reload для ассетов
Отслеживание изменений файлов и автоматическая перезагрузка.

### 4. Memory Budget
Ограничение памяти для ассетов с автоматической выгрузкой (LRU cache).

### 5. Asset References
Добавить слабые ссылки для предотвращения удержания памяти:
```csharp
public class AssetRef<T> where T : Asset
{
    private WeakReference<T>? _weakRef;
    private string _path;
    
    public T? Value => _weakRef?.TryGetTarget(out var target) == true ? target : Load();
}
```

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Исправить race condition при загрузке
- [ ] CR-002: Добавить проверки для Activator.CreateInstance
- [ ] HI-001: Убрать LINQ из рефлексии
- [ ] HI-002: Добавить null-проверку для _serviceProvider
- [ ] HI-003: Исправить проблему с копированием AssetHandle
- [ ] HI-004: Добавить обработку исключений при загрузке
- [ ] MI-001: Вынести логику поиска пути в отдельный метод
- [ ] MI-002: Оптимизировать поиск по расширению
- [ ] MI-003: Убрать избыточное поле Type
- [ ] MI-004: Заменить рекурсию на итерацию
