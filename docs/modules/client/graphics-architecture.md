# Архитектура Graphics модуля (Veldrid)

## Ключевые принципы

- **Zero Allocation** в hot path — приоритет коду без аллокаций в кадре
- **Thread-Local буферы** — потоки пишут без блокировок
- **Написать один раз** — не переписывать под 3D
- **Veldrid forever** — свой форк, полный контроль

---

## 1. Command Buffer Architecture

### Выбрано: Thread-Local Buffers → Merge

**Описание:**
- Каждый рабочий поток (Job System) получает свой локальный буфер команд
- Запись в буфер происходит без блокировок (каждый поток работает со своим буфером)
- В конце кадра все буферы сливаются (merge) в единый CommandList
- Merge выполняется асинхронно в отдельном потоке

**Почему:**
| Критерий | Почему |
|----------|--------|
| Zero Allocation | Буферы создаются один раз при старте, переиспользуются |
| Cache-friendly | Данные одного потока линейны в памяти, нет false sharing |
| Без блокировок | Потоки пишут в изолированные буфера |
| Scalable | Добавили 3D — merge работает так же |

---

## 2. Модель синхронизации

### Выбрано: Async Merge

**Описание:**
```
Main Thread              Worker Threads              Merge Thread
──────────────────────────────────────────────────────────────────
BeginFrame()         
  │                 GetBuffer() → write commands
  │                        │
  │                        ▼
  │                 [thread-local buffer]
  │                        │
  │                        ... (multiple jobs)
  │                        │
EndFrame() ──────────────┼─────────────────────► Merge()
  │                  all thread buffers          │
  │                                               │
  │                                               ▼
  │                                       [unified CommandList]
  │                                               │
Submit() ◄────────────────────────────────────────┘
  │
GPU
```

**Почему:**
- Не блокирует главный поток
- Масштабируется с ростом количества команд
- Легко адаптировать под Vulkan/DirectX
- Соответствует современным практикам в графических API

---

## 3. API для записи команд

### Выбрано: Struct-based Commands

**Описание:**
```csharp
// Команды — value types (struct)
public struct DrawRectCmd { RectangleF Rect; Color Color; }
public struct DrawTextureCmd { ITexture2D Texture; Vector2 Position; Color Color; }
public struct DrawTextCmd { IFont Font; ReadOnlySpan<char> Text; Vector2 Position; float Size; Color Color; }

// Буфер принимает любую команду
public interface ICommandBuffer
{
    void Add<T>(in T cmd) where T : struct;
}
```

**Почему:**
| Критерий | Почему |
|----------|--------|
| Zero Allocation | `void` return, value type |
| DOD | Структуры линейны в памяти — cache-friendly |
| Расширяемость | Новая команда — новая struct, интерфейс буфера не меняется |
| Debuggable | Можно посмотреть содержимое в отладчике |

---

## 4. Абстракция от Veldrid

### Выбрано: Veldrid Visible

**Описание:**
```csharp
// Thin wrapper, Veldrid типы на поверхности
public using Texture = Veldrid.Texture;
public using Shader = Veldrid.Shader;
public using CommandList = Veldrid.CommandList;

struct DrawRectCmd { ... }
struct DrawTextureCmd { ... }

interface ICommandBuffer
{
    void Add(in DrawRectCmd cmd);
    void Add(in DrawTextureCmd cmd);
}
```

**Почему:**
- Veldrid (свой форк) — это forever
- Свой форк = полный контроль
- Максимальная производительность без overhead
- Не нужна абстракция которая не понадобится

---

## 5. Жизненный цикл ресурсов

### Выбрано: Отдельные фабрики

**Описание:**
```csharp
interface ITextureFactory
{
    ITexture2D Create(TextureDescription desc);
    void Destroy(ITexture2D texture);
}

interface IShaderFactory
{
    IShader Create(ShaderDescription desc);
}

interface IPipelineFactory
{
    IPipeline Create(PipelineDescription desc);
}

interface IGraphicsContext
{
    ITextureFactory Textures { get; }
    IShaderFactory Shaders { get; }
    IPipelineFactory Pipelines { get; }
}
```

**Почему:**
- Separation of concerns
- Тестируемость (можно подменить фабрику моком)
- Расширяемость (добавили ресурс — добавили фабрику)
- Соответствует принципу модульности KarpikEngine

---

## 6. Синхронизация с игровым циклом

### Выбрано: Модуль полностью управляет

**Описание:**
```csharp
// GraphicsModule
public void Init() 
{ 
    graphicsContext.Init(); 
    mergeThread.Start();
}

public void BeginFrame() 
{ 
    graphicsContext.BeginFrame();  // разблокировать буферы
}

public void Update() 
{ 
    jobSystem.Run();  // jobs пишут в thread-local буферы
}

public void EndFrame() 
{ 
    mergeThread.MergeAndSignal();            // асинхронный merge
    graphicsContext.SubmitWhenReady();      // submit когда merge готов
}
```

**Почему:**
- "Точно вызовутся" — пользователь не забудет
- Пользователь только пишет команды в буфер
- Нет публичного Begin/End — internal workflow модуля

---

## 7. Обработка ошибок

### Выбрано: Error Queue

**Описание:**
```csharp
// В любом потоке при записи команды:
buffer.Add(cmd);  // internal проверка

// В главном потоке, после submit:
while (graphicsContext.ErrorQueue.TryDequeue(out var error))
{
    Logger.Error(error);
}
```

**Почему:**
| Критерий | Почему |
|----------|--------|
| Zero allocation | Нет exceptions в happy path |
| Работает из worker thread | Асинхронная модель |
| Минимальный overhead | Queue всегда нативный |

---

## 8. Поверхность API (для пользователя)

### Выбрано: ThreadStatic accessor

**Описание:**
```csharp
// Пользователь пишет в ECS системе:
var buffer = GraphicsContext.Buffer;
buffer.Add(new DrawRectCmd { Rect = rect, Color = color });

// Internal:
// GraphicsContext — static class с [ThreadStatic] буфером
public static class GraphicsContext
{
    [ThreadStatic]
    private static ICommandBuffer _buffer;
    
    public static ICommandBuffer Buffer
    {
        get
        {
            if (_buffer == null || _buffer.FrameId != _currentFrameId)
            {
                _buffer = new ThreadBuffer(_currentFrameId);
            }
            return _buffer;
        }
    }
}
```

**Почему:**
- Просто: одна строка
- Zero allocation
- Работает из любого потока
- Auto-init — буфер создаётся при первом обращении

---

## 9. Набор команд (2D)

### Выбрано: Минимальный набор

**Описание:**
```csharp
public struct DrawRectCmd 
{ 
    public RectangleF Rect; 
    public Color Color; 
}

public struct DrawTextureCmd 
{ 
    public ITexture2D Texture; 
    public Vector2 Position; 
    public Color Color; 
}

public struct DrawTextCmd 
{ 
    public IFont Font; 
    public ReadOnlySpan<char> Text; 
    public Vector2 Position; 
    public float Size; 
    public Color Color; 
}
```

**Почему:**
- Достаточно для базового 2D
- Расширяется при необходимости (трансформации, линии, круги)
- Не переусложняем сразу

---

## Архитектура: итоговая схема

```
┌─────────────────────────────────────────────────────────────────┐
│                      GraphicsModule                              │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │ Init() → BeginFrame() → Update() → EndFrame()           │  │
│  └──────────────────────────────────────────────────────────┘  │
│                              │                                   │
│              ┌───────────────┼───────────────┐                   │
│              ▼               ▼               ▼                   │
│     ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│     │   Factories │  │    Merge    │  │   Submit    │            │
│     │  (resources)│  │   Thread    │  │   (GPU)     │            │
│     └─────────────┘  └─────────────┘  └─────────────┘            │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ ThreadStatic
┌─────────────────────────────────────────────────────────────────┐
│                   GraphicsContext.Buffer                         │
│                                                                 │
│   Worker Thread 1          Worker Thread 2     Worker Thread N │
│   ┌─────────────┐          ┌─────────────┐       ┌─────────────┐│
│   │Buffer(Type1)│          │Buffer(Type1)│       │Buffer(Type1)││
│   │Commands [...]│        │Commands [...]│      │Commands [...]││
│   └─────────────┘          └─────────────┘       └─────────────┘│
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Add()
┌─────────────────────────────────────────────────────────────────┐
│                     DrawCommand structs                          │
│   ┌────────────┐  ┌────────────┐  ┌────────────┐                │
│   │ DrawRect   │  │ DrawTexture│  │  DrawText  │   ...         │
│   └────────────┘  └────────────┘  └────────────┘                │
└─────────────────────────────────────────────────────────────────┘
```

---

## Usage пример

```csharp
// ECS render-command система. В 0.4 выполняется последовательно.
public class SpriteRenderSystem : ISystemRender
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> Position = Inc;
        public EcsReadonlyPool<Sprite> Sprite = Inc;
    }

    [DI] private DefaultWorld _world = null!;
    
    public void Render()
    {
        var buffer = GraphicsContext.Buffer;
        
        foreach (var entity in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.Position.Get(entity);
            ref readonly var sprite = ref a.Sprite.Get(entity);
            
            buffer.Add(new DrawTextureCmd
            {
                Texture = sprite.Texture,
                Position = new Vector2(pos.X, pos.Y),
                Color = Color.White
            });
        }
    }
}
```

---

## Тестирование

### Что можно тестировать

| Что тестируем | Как | Пример |
|---------------|-----|--------|
| **Команды** | Unit | `DrawRectCmd` — правильные поля, сериализация, equality |
| **Фабрики** | Unit + Mock | `TextureFactory.Create()` — создаёт с правильными параметрами |
| **Сортировка** | Unit | `CommandSorter.Sort(commands)` — правильный порядок |
| **ThreadBuffer** | Unit | `Add()`, `Clear()`, счётчик команд, frame tracking |
| **Жизненный цикл** | Integration | `GraphicsModule.Init/Begin/End` — правильная последовательность |

### Что НЕ тестируем

- Реальный рендеринг — требует GPU
- Визуальные баги — надо смотреть глазами
- Производительность — benchmark, не тест

### Подход: Mock Veldrid

```csharp
// Тестовый Veldrid, который не обращается к GPU
public class MockGraphicsDevice : GraphicsDevice
{
    public override Texture CreateTexture(TextureDescription desc)
        => new MockTexture(desc);

    public override Shader CreateShader(ShaderDescription desc)
        => new MockShader(desc);
}

// Тест:
[Fact]
public void TextureFactory_Create_CallsGraphicsDevice()
{
    var mockDevice = new MockGraphicsDevice();
    var factory = new TextureFactory(mockDevice);

    var texture = factory.Create(new TextureDescription { Width = 1024, Height = 1024 });

    Assert.NotNull(texture);
    Assert.Equal(1024, texture.Width);
}
```

### Подход: Property-Based Testing

```csharp
[Property]
public void CommandBuffer_Add_StoresCorrectData()
{
    var buffer = new ThreadBuffer();

    var cmd = new DrawRectCmd { Rect = rect, Color = color };
    buffer.Add(cmd);

    Assert.Single(buffer.Commands);
    Assert.Equal(rect, buffer.Commands[0].Rect);
    Assert.Equal(color, buffer.Commands[0].Color);
}
```

### Рекомендация

**Минимум:**
- Unit тесты команд (правильность полей, equality, hash)
- Unit тесты фабрик (с моком Veldrid)
- Интеграционные тесты жизненного цикла модуля

**Со временем:**
- Golden tests для базовых примитивов (rect, circle, line)

---

## Структура проекта

### Graphics.Core (интерфейсы, абстракции)

```
Graphics.Core/
├── Commands/
│   ├── DrawRectCmd.cs
│   ├── DrawTextureCmd.cs
│   └── DrawTextCmd.cs
├── Buffers/
│   └── ICommandBuffer.cs
├── Factories/
│   ├── ITextureFactory.cs
│   ├── IShaderFactory.cs
│   └── IPipelineFactory.cs
├── Context/
│   └── IGraphicsContext.cs
├── Resources/
│   ├── ITexture2D.cs
│   ├── IShader.cs
│   └── IPipeline.cs
└── GraphicsModule.cs
```

### Graphics.Veldrid (реализация)

```
Graphics.Veldrid/
├── VeldridCommandBuffer.cs
├── VeldridTextureFactory.cs
├── VeldridShaderFactory.cs
├── VeldridPipelineFactory.cs
├── VeldridGraphicsContext.cs
├── ThreadBuffer.cs
├── MergeThread.cs
└── VeldridGraphicsModule.cs
```

---

## Детали реализации интерфейсов

### Commands

```csharp
// DrawRectCmd.cs
public struct DrawRectCmd
{
    public RectangleF Rect;
    public Color Color;
}

// DrawTextureCmd.cs
public struct DrawTextureCmd
{
    public ITexture2D Texture;
    public Vector2 Position;
    public Color Color;
}

// DrawTextCmd.cs
public struct DrawTextCmd
{
    public IFont Font;
    public ReadOnlySpan<char> Text;
    public Vector2 Position;
    public float Size;
    public Color Color;
}
```

### Buffers

```csharp
// ICommandBuffer.cs
public interface ICommandBuffer
{
    int FrameId { get; }
    int Count { get; }
    
    void Add<T>(in T cmd) where T : struct;
    void Clear();
    ReadOnlySpan<DrawCommand> GetCommands();
}
```

### Factories

```csharp
// ITextureFactory.cs
public interface ITextureFactory
{
    ITexture2D Create(TextureDescription desc);
    void Destroy(ITexture2D texture);
}

// IShaderFactory.cs
public interface IShaderFactory
{
    IShader Create(ShaderDescription desc);
    void Destroy(IShader shader);
}

// IPipelineFactory.cs
public interface IPipelineFactory
{
    IPipeline Create(PipelineDescription desc);
    void Destroy(IPipeline pipeline);
}
```

### Resources (интерфейсы-маркеры)

```csharp
public interface ITexture2D { }

// IShader — для кастомных шейдеров (опционально)
public interface IShader { }

// IPipeline — для готовых pipeline (опционально)
public interface IPipeline { }
```

### Context

```csharp
// IGraphicsContext.cs
public interface IGraphicsContext
{
    ITextureFactory Textures { get; }
    IShaderFactory Shaders { get; }
    IPipelineFactory Pipelines { get; }
    
    void Init();
    void BeginFrame();
    void Submit();
}
```

### GraphicsContext (static accessor)

```csharp
// GraphicsContext.cs (публичный API)
public static class GraphicsContext
{
    private static int _currentFrameId;
    private static readonly object _lock = new();
    
    [ThreadStatic]
    private static ICommandBuffer _buffer;
    
    public static ICommandBuffer Buffer
    {
        get
        {
            if (_buffer == null || _buffer.FrameId != _currentFrameId)
            {
                _buffer = CreateBuffer(_currentFrameId);
            }
            return _buffer;
        }
    }
    
    internal static void BeginFrame()
    {
        lock (_lock)
        {
            _currentFrameId++;
        }
    }
    
    internal static ICommandBuffer[] CollectBuffers()
    {
        // Сбор всех thread-local буферов
    }
}
```

---

## План реализации

### Этап 1: Базовая структура (интерфейсы)

- [ ] Создать `Graphics.Core/Commands/` — структуры команд
- [ ] Создать `Graphics.Core/Buffers/ICommandBuffer.cs`
- [ ] Создать `Graphics.Core/Factories/` — интерфейсы фабрик
- [ ] Создать `Graphics.Core/Resources/` — интерфейсы-маркеры
- [ ] Создать `Graphics.Core/Context/IGraphicsContext.cs`
- [ ] Тесты: Unit тесты команд

### Этап 2: ThreadBuffer + GraphicsContext

- [ ] Создать `Graphics.Veldrid/ThreadBuffer.cs` — реализация
- [ ] Создать `Graphics.Veldrid/VeldridCommandBuffer.cs`
- [ ] Создать `Graphics.Core/Context/GraphicsContext.cs` — static accessor
- [ ] Реализовать lazy reset по frame ID
- [ ] Тесты: ThreadBuffer логика

### Этап 3: Фабрики (Veldrid)

- [ ] Создать `Graphics.Veldrid/VeldridTextureFactory.cs`
- [ ] Создать `Graphics.Veldrid/VeldridShaderFactory.cs`
- [ ] Создать `Graphics.Veldrid/VeldridPipelineFactory.cs`
- [ ] Создать `Graphics.Veldrid/VeldridGraphicsContext.cs`
- [ ] Тесты: Фабрики с mock

### Этап 4: MergeThread

- [ ] Создать интерфейс `Graphics.Core/IMergeThread`
- [ ] Реализовать асинхронный merge
- [ ] Интеграция с Veldrid CommandList
- [ ] Тесты: Merge логика

### Этап 5: GraphicsModule

- [ ] Создать `Graphics.Veldrid/VeldridGraphicsModule.cs`
- [ ] Реализовать `IModule`
- [ ] Интеграция с ECS (DI)
- [ ] Жизненный цикл (Init/Begin/Update/End)
- [ ] Тесты: Интеграционные тесты модуля

---

## Зависимости

- **Graphics.Core** → Veldrid (субмодуль)
- **Graphics.Veldrid** → Graphics.Core, Veldrid

---

## Что не вошло в документ (TODO / На будущее)

- Пакетирование (batching) команд
- UI рендеринг поверх игры
- Post-processing эффекты
- Render targets / offscreen рендеринг
- Debug tooling (visualization буферов)

Эти темы можно раскрыть после базовой реализации.
