using System.Collections.Concurrent;
using System.Reflection;
using Karpik.Jobs;
using Xunit;

namespace Karpik.Jobs.Tests;

public sealed class JobSystemCompatibilityBoundaryTests
{
    [Fact]
    public void JobSystem_IsMarkedAsAllocatingCompatibilityPath()
    {
        AllocatingCompatibilityAttribute attribute =
            Assert.IsType<AllocatingCompatibilityAttribute>(
                Attribute.GetCustomAttribute(typeof(JobSystem), typeof(AllocatingCompatibilityAttribute)));

        Assert.True(attribute.AllocatesManagedMemory);
        Assert.False(attribute.IsHotPathSafe);
    }

    [Fact]
    public void Constructor_IsMarkedAsAllocatingCompatibilityPath()
    {
        ConstructorInfo constructor = Assert.Single(typeof(JobSystem).GetConstructors());

        AllocatingCompatibilityAttribute attribute =
            Assert.IsType<AllocatingCompatibilityAttribute>(
                Attribute.GetCustomAttribute(constructor, typeof(AllocatingCompatibilityAttribute)));

        Assert.True(attribute.AllocatesManagedMemory);
        Assert.False(attribute.IsHotPathSafe);
    }

    [Fact]
    public void DelegateCompatibilityApis_AreMarkedAsAllocatingCompatibilityPath()
    {
        MethodInfo[] methods = typeof(JobSystem)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name is nameof(JobSystem.Enqueue) or nameof(JobSystem.EnqueueParallel) or nameof(JobSystem.Combine))
            .ToArray();

        Assert.NotEmpty(methods);
        foreach (MethodInfo method in methods)
        {
            AllocatingCompatibilityAttribute attribute =
                Assert.IsType<AllocatingCompatibilityAttribute>(
                    Attribute.GetCustomAttribute(method, typeof(AllocatingCompatibilityAttribute)));

            Assert.True(attribute.AllocatesManagedMemory);
            Assert.False(attribute.IsHotPathSafe);
        }
    }

    [Fact]
    public void ValueJobScheduler_DoesNotReferenceLegacyDelegateCompatibilityTypes()
    {
        Type[] forbiddenTypes =
        [
            typeof(JobSystem),
            typeof(JobHandle),
            typeof(JobWrapper),
            typeof(JobCompletion),
            typeof(CancellationTokenSource),
            typeof(ConcurrentQueue<JobWrapper>)
        ];

        FieldInfo[] fields = typeof(JobScheduler)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (FieldInfo field in fields)
        {
            Assert.DoesNotContain(forbiddenTypes, forbidden => ContainsType(field.FieldType, forbidden));
        }
    }

    private static bool ContainsType(Type candidate, Type forbidden)
    {
        if (candidate == forbidden)
        {
            return true;
        }

        if (candidate.IsArray)
        {
            Type? elementType = candidate.GetElementType();
            return elementType is not null && ContainsType(elementType, forbidden);
        }

        if (!candidate.IsGenericType)
        {
            return false;
        }

        foreach (Type argument in candidate.GetGenericArguments())
        {
            if (ContainsType(argument, forbidden))
            {
                return true;
            }
        }

        return false;
    }
}
