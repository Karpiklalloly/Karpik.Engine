using System.Composition;
using System.Reflection;
using Autofac;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Dynamic-mode only: registers attributed services by scanning <see cref="Type"/>
/// metadata at runtime. Static composition must never enter this path; generated
/// factories are applied through <see cref="AutofacStaticServiceRegistry"/> instead.
/// </summary>
internal static class AttributedServiceRegistrar
{
    public static void Register(
        ContainerBuilder builder,
        IEnumerable<Type> types,
        ModuleScope scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(types);

        foreach (Type type in types)
        {
            ServiceRegistrationAttribute? service =
                type.GetCustomAttribute<ServiceRegistrationAttribute>();
            if (service is null || service.Scope != scope)
            {
                continue;
            }

            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Exported service '{type.FullName}' must be a concrete, closed class.");
            }

            ExportAttribute[] exports = type.GetCustomAttributes<ExportAttribute>().ToArray();
            if (exports.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Service '{type.FullName}' has {nameof(ServiceRegistrationAttribute)} but no {nameof(ExportAttribute)}.");
            }

            var registration = builder.RegisterType(type);
            foreach (ExportAttribute export in exports)
            {
                registration.As(export.ContractType ?? type);
            }
            if (typeof(IStartable).IsAssignableFrom(type))
            {
                registration.As<IStartable>();
            }

            switch (service.Lifetime)
            {
                case ServiceLifetime.Singleton:
                    registration.SingleInstance();
                    break;
                case ServiceLifetime.Transient:
                    registration.InstancePerDependency();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(service.Lifetime),
                        service.Lifetime,
                        "Unsupported service lifetime.");
            }
        }
    }
}
