﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless.Core.Helpers;
using Exceptionless.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Exceptionless.Core.Extensions {
    public static class ContainerExtensions {
        public static void AddSingleton(this IServiceCollection services, Type type, params Assembly[] assemblies) {
            var implementingTypes = new List<Type>();
            implementingTypes.AddRange(type.IsGenericTypeDefinition
                ? TypeHelper.GetAllTypesImplementingOpenGenericType(type, assemblies)
                : TypeHelper.GetDerivedTypes(type, assemblies));

            foreach (var implementingType in implementingTypes) {
                var registrationType = type;
                if (type.IsGenericTypeDefinition)
                    registrationType = type.MakeGenericType(implementingType.BaseType.GenericTypeArguments);

                services.AddSingleton(registrationType, implementingType);
                services.AddSingleton(implementingType, implementingType);
            }
        }

        public static IServiceCollection ReplaceSingleton<T>(this IServiceCollection services, T instance) {
            return services.Replace(new ServiceDescriptor(typeof(T), s => instance, ServiceLifetime.Singleton));
        }

        public static IServiceCollection ReplaceSingleton<T>(this IServiceCollection services, object instance) {
            return services.Replace(new ServiceDescriptor(typeof(T), s => instance, ServiceLifetime.Singleton));
        }

        public static IServiceCollection ReplaceSingleton<T>(this IServiceCollection services, Func<IServiceProvider, object> factory) {
            return services.Replace(new ServiceDescriptor(typeof(T), factory, ServiceLifetime.Singleton));
        }

        public static IServiceCollection ReplaceSingleton<T>(this IServiceCollection services, Func<IServiceProvider, T> factory) {
            return services.Replace(new ServiceDescriptor(typeof(T), s => factory(s), ServiceLifetime.Singleton));
        }

        public static IServiceCollection ReplaceSingleton<TService, TInstance>(this IServiceCollection services) {
            return services.Replace(new ServiceDescriptor(typeof(TService), typeof(TInstance), ServiceLifetime.Singleton));
        }

        public static async Task RunStartupActionsAsync(this IServiceProvider container, CancellationToken shutdownToken = default) {
            foreach (var startupAction in container.GetServices<StartupActionRegistration>().OrderBy(s => s.Priority))
                await startupAction.RunAsync(container, shutdownToken).AnyContext();
        }

        public static void AddStartupAction<T>(this IServiceCollection container, int? priority = null) where T : IStartupAction {
            container.AddTransient(s => new StartupActionRegistration(typeof(T), priority));
        }

        public static void AddStartupAction(this IServiceCollection container, Action action, int? priority = null) {
            AddStartupAction(container, ct => action(), priority);
        }

        public static void AddStartupAction(this IServiceCollection container, Action<IServiceProvider> action, int? priority = null) {
            AddStartupAction(container, (sp, ct) => action(sp), priority);
        }

        public static void AddStartupAction(this IServiceCollection container, Action<IServiceProvider, CancellationToken> action, int? priority = null) {
            container.AddTransient(s => new StartupActionRegistration((sp, ct) => {
                action(sp, ct);
                return Task.CompletedTask;
            }, priority));
        }

        public static void AddStartupAction(this IServiceCollection container, Func<Task> action, int? priority = null) {
            container.AddStartupAction((sp, ct) => action(), priority);
        }

        public static void AddStartupAction(this IServiceCollection container, Func<IServiceProvider, Task> action, int? priority = null) {
            container.AddStartupAction((sp, ct) => action(sp), priority);
        }

        public static void AddStartupAction(this IServiceCollection container, Func<IServiceProvider, CancellationToken, Task> action, int? priority = null) {
            container.AddTransient(s => new StartupActionRegistration(action, priority));
        }

        private class StartupActionRegistration {
            private readonly Func<IServiceProvider, CancellationToken, Task> _action;
            private readonly Type _actionType;
            private static int _currentAutoPriority;

            public StartupActionRegistration(Type startupType, int? priority = null) {
                _actionType = startupType;
                if (!priority.HasValue) {
                    var priorityAttribute = _actionType.GetCustomAttributes(typeof(PriorityAttribute), true).FirstOrDefault() as PriorityAttribute;
                    Priority = priorityAttribute?.Priority ?? Interlocked.Increment(ref _currentAutoPriority);
                } else {
                    Priority = priority.Value;
                }
            }

            public StartupActionRegistration(Func<IServiceProvider, CancellationToken, Task> action, int? priority = null) {
                _action = action;
                if (!priority.HasValue)
                    priority = Interlocked.Increment(ref _currentAutoPriority);

                Priority = priority.Value;
            }

            public int Priority { get; private set; }

            public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken shutdownToken = default) {
                if (_actionType != null) {
                    if (serviceProvider.GetRequiredService(_actionType) is IStartupAction startup)
                        await startup.RunAsync(shutdownToken).AnyContext();
                } else {
                    await _action(serviceProvider, shutdownToken).AnyContext();
                }
            }
        }
    }

    public interface IStartupAction {
        Task RunAsync(CancellationToken shutdownToken = default);
    }
}
