# Veldrid-like Graphics Architecture for KarpikEngine

## Core Concept

**Отказ от модели конечного автомата (OpenGL) в пользу модели Command Buffers (Vulkan/DX12)**

```
Recording (Запись):     Параллельно в Worker-потоках → пишут в локальный CommandList
Submission (Отправка): Главный поток → собирает CommandList → отправляет на GPU разом
```

---

## Main Entities

### GraphicsDevice
- **Назначение**: Управление GPU, создание ресурсов, выполнение Submit()
- **Поток**: Главный поток
- **Методы**:
  - `CreateCommandList()` — фабрика буферов команд
  - `CreateBuffer()` / `CreateTexture()` / `CreateGraphicsPipeline()` — создание ресурсов
  - `Submit()` — отправка команд на GPU
  - `SwapBuffers()` — смена кадра
  - `WaitForIdle()` — синхронизация CPU-GPU

### CommandList
- **Назначение**: Буфер команд рендеринга
- **Поток**: Один экземпляр на рабочий поток
- **Жизненный цикл**:
  1. `Begin()` — начало записи
  2. Запись команд (draw, set pipeline, bind resources...)
  3. `End()` — завершение записи
  4. `Submit()` → GPU
- **Важно**: Не потокобезопасен — один CommandList на поток

### CommandListPool
- **Назначение**: Пул для многопоточного использования CommandList
- **Реализация**: Lock-free через `ConcurrentBag`
- **Методы**:
  - `Rent()` — получить список из пула (автоматически вызывает Begin)
  - `Return()` — вернуть список в пул

### GraphicsContext
- **Назначение**: Центральный сервис управления отрисовкой
- **Поток**: Один экземпляр, внедряется через DI
- **Методы**:
  - `BeginCommandList()` — получить CommandList для текущего потока
  - `SubmitCommandList()` — поставить в очередь на отправку
  - `ExecuteFrame()` — выполнить ВСЕ команды на GPU (вызывается в конце кадра)
- **Zero-Allocation**: Использует `ArrayPool` для batch submit

### Pipeline
- **Назначение**: Инкапсуляция шейдеров и состояний рендеринга
- **Свойство**: Строго иммутабелен — безопасен для многопоточного биндинга
- **Содержит**:
  - Vertex/Fragment шейдеры
  - Vertex layout (формат вершин)
  - Resource layouts (какие uniform-буферы и текстуры ожидает шейдер)
  - RasterizerState (cull mode, fill mode)
  - DepthStencilState (тест глубины, stencil)
  - BlendState (альфа-блендинг)
  - Output formats (формат color/depth буферов)

### DeviceBuffer
- **Назначение**: GPU-память для вершин/индексов/uniforms
- **Свойства**: SizeInBytes, BufferUsage (Static/Dynamic/Vertex/Index)

### Texture
- **Назначение**: GPU-память для текстур
- **Свойства**: Width, Height, PixelFormat

### Framebuffer
- **Назначение**: Render target — куда рендерим
- **Содержит**: ColorTargets[] + DepthTarget

### ResourceLayout
- **Назначение**: Описание слотов для биндинга (какие слоты буферов/текстур использует шейдер)
- **Примечание**: Opaque handle — пользователь не создаёт напрямую

### ResourceSet
- **Назначение**: Набор связанных ресурсов для пайплайна
- **Содержит**: Layout + BoundResources[] (конкретные буферы и текстуры)

---

## Backend Implementation

### Modern API (Vulkan, DX12, Metal)
- CommandList напрямую маппится на нативные многопоточные команд-буферы

### Legacy API (OpenGL, DX11, Raylib)
- Требуют "Deferred Execution"
- CommandList пишет команды в программный массив (List) на CPU
- Во время Submit() главный поток проходится по массиву и делает реальные вызовы
- **Текущая реализация**: Raylib backend использует Deferred Execution

---

## Technical Requirements

### Render Passes
- Строгое определение начала/конца прохода рендеринга
- Очистка, target-фреймбуферы

### Staging Buffers
- Механизм транзитных буферов для асинхронной загрузки данных с CPU на GPU потоками

### Resource Synchronization
- Fences (заборы) для отслеживания кадров
- Disposal Queue для предотвращения удаления буферов, которые ещё используются GPU

---

## Implementation Plan

### Phase 1: Low-level ✅ Реализовано
```
1. CommandList + CommandListPool ✅
   - Интерфейс ICommandList
   - Реализация для Raylib (Deferred Execution - массив команд)
   - Пул с предсозданными экземплярами (ConcurrentBag, lock-free)

2. Pipeline (иммутабельный) ✅
   - Интерфейс IPipeline
   - Конфиг (вершинный формат, шейдеры, blend state, depth state, rasterizer state)

3. GraphicsContext ✅
   - Сервис для получения/возврата CommandList
   - ExecuteFrame() → GraphicsDevice.Submit()
```

### Phase 2: High-level (B)
```
High-level extension methods для ICommandList:
- DrawSprite(Sprite, Transform)
- DrawText(Text, Transform)
- DrawMesh(Mesh, Material)
- и т.д.
```

---

## Integration with ECS

```csharp
public class SpriteRenderSystem : IEcsRunParallel
{
    [DI] private GraphicsContext _ctx;
    
    public void RunParallel()
    {
        var cmd = _ctx.BeginCommandList();
        
        foreach (var (sprite, transform) in Query...)
        {
            cmd.DrawSprite(sprite, transform);
        }
        
        _ctx.SubmitCommandList(cmd);  // в очередь, НЕ сразу на GPU
    }
}

// Где-то в главном потоке, в конце кадра:
_graphicsContext.ExecuteFrame();  // отправить ВСЕ на GPU
```

---

## Assets Management

- **AssetsManager** загружает и кэширует ресурсы
- **Reference counting** — удаление если никто не ссылается
- **Хранитель ссылки** — любой: сервис или ECS сущность

---

## Zero-Allocation Patterns

| Компонент | Паттерн |
|-----------|---------|
| CommandListPool | `ConcurrentBag<ICommandList>` — lock-free |
| GraphicsContext.ExecuteFrame | `ArrayPool<ICommandList>.Shared.Rent(count)` |
| FramebufferDescription.ColorTargets | `ReadOnlyMemory<ITexture>` |
| IGraphicsResource.SetDebugName | `void SetDebugName(ReadOnlySpan<char>)` — без string аллокаций |
| ICommandList.UpdateBuffer | `ReadOnlySpan<T>` + `where T : unmanaged` |

---

## Status

- [x] Phase 1: CommandList + Pool + GraphicsContext
- [ ] Phase 2: High-level extensions
- [ ] Raylib backend реализация