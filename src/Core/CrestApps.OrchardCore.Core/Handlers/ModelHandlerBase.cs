using CrestApps.OrchardCore.Models;
using CrestApps.OrchardCore.Services;

namespace CrestApps.OrchardCore.Core.Handlers;

public abstract class ModelHandlerBase<T> : ICatalogEntryHandler<T>
{
    public virtual Task DeletedAsync(DeletedContext<T> context)
        => Task.CompletedTask;

    public virtual Task DeletingAsync(DeletingContext<T> context)
        => Task.CompletedTask;

    public virtual Task InitializedAsync(InitializedContext<T> context)
        => Task.CompletedTask;

    public virtual Task InitializingAsync(InitializingContext<T> context)
        => Task.CompletedTask;

    public virtual Task LoadedAsync(LoadedContext<T> context)
        => Task.CompletedTask;

    public virtual Task CreatedAsync(CreatedContext<T> context)
        => Task.CompletedTask;

    public virtual Task CreatingAsync(CreatingContext<T> context)
        => Task.CompletedTask;

    public virtual Task UpdatedAsync(UpdatedContext<T> context)
        => Task.CompletedTask;

    public virtual Task UpdatingAsync(UpdatingContext<T> context)
        => Task.CompletedTask;

    public virtual Task ValidatedAsync(ValidatedContext<T> context)
        => Task.CompletedTask;

    public virtual Task ValidatingAsync(ValidatingContext<T> context)
        => Task.CompletedTask;
}
