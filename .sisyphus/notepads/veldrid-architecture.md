# Veldrid-like Graphics Architecture for KarpikEngine

## Core Concept

**Отказ от модели конечного автомата (OpenGL) в пользу модели Command Buffers (Vulkan/DX12)**

```
Recording (Запись):     Параллельно в Worker-потоках → пишут в локальный CommandList
Submission (Отправка):   Главный поток → собирает CommandList → отправляет на GPU разом
```

---

## Main Entities

### GraphicsDevice
- Управляет GPU
- Создаёт ресурсы
- Выполняет Submit()
- Живёт в главном потоке

### CommandList
- Буфер команд
- Не потокобезопасен сам по себе
- Создаётся по 1 экземпляру на каждый рабочий поток

### Pipeline
- Инкапсулирует шейдеры и стейты (Blend, Depth)
- Строго иммутабелен для потокобезопасного биндинга

### DeviceBuffer / Texture
- Ресурсы в памяти GPU

### ResourceSet
- Группировка биндингов (Uniforms, Textures) для пайплайна

---

## Backend Implementation

### Modern API (Vulkan, DX12, Metal)
- CommandList напрямую маппится на нативные многопоточные команд-буферы

### Legacy API (OpenGL, DX11)
- Требуют "Deferred Execution"
- CommandList пишет команды в программный массив (List) на CPU
- Во время Submit() главный поток проходится по массиву и делает реальные вызовы

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

### Phase 1: Low-level (A)
```
1. CommandList + CommandListPool
   - Интерфейс ICommandList
   - Реализация для Raylib (Deferred Execution - массив команд)
   - Пул с предсозданными экземплярами (N штук, расширяется)

2. Pipeline (иммутабельный)
   - Интерфейс IPipeline
   - Конфиг (вершинный формат, шейдеры, blend state, depth state)

3. CommandListQueue / GraphicsContext
   - Сервис для получения/возврата CommandList
   - SubmitAll() → GraphicsDevice
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
public interface IGraphicsContext
{
    ICommandList GetCommandList();  // получить из пула
    void Submit(ICommandList cmd);   // в очередь
}

// RunnerSystem.Run():
_fixedRun.Invoke();
_parallelRunner.RunParallel();
_graphicsContext.SubmitAll();  // отправить ВСЕ на GPU после Parallel
```

```csharp
// Система:
public class SpriteRenderSystem : IEcsRunParallel
{
    [DI] private IGraphicsContext _ctx;
    
    public void RunParallel()
    {
        var cmd = _ctx.GetCommandList();
        
        foreach (var (sprite, transform) in Query...)
        {
            cmd.DrawSprite(sprite, transform);
        }
        
        _ctx.Submit(cmd);  // в очередь, НЕ сразу на GPU
    }
}
```

---

## Assets Management

- **AssetsManager** загружает и кэширует ресурсы
- **Reference counting** — удаление если никто не ссылается
- **Хранитель ссылки** — любой: сервис или ECS сущность

---

## Open Questions

1. CommandListPool — фиксированный размер или динамическое расширение?
2. SubmitAll() вызывается синхронно после RunParallel
3. GraphicsContext — отдельный сервис

---

## Status

- [ ] Phase 1: CommandList + Pool + GraphicsContext
- [ ] Phase 2: High-level extensions
- [ ] Интеграция с существующим RaylibRenderer