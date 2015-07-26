﻿#region Copyright Simple Injector Contributors
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (c) 2015 Simple Injector Contributors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
 * associated documentation files (the "Software"), to deal in the Software without restriction, including 
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the 
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial 
 * portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO 
 * EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion

namespace SimpleInjector.Internals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal sealed class NonGenericRegistrationEntry : IRegistrationEntry
    {
        private readonly List<IProducerProvider> providers = new List<IProducerProvider>(1);
        private readonly Type nonGenericServiceType;
        private readonly Container container;

        public NonGenericRegistrationEntry(Type nonGenericServiceType, Container container)
        {
            this.nonGenericServiceType = nonGenericServiceType;
            this.container = container;
        }

        private interface IProducerProvider
        {
            IEnumerable<InstanceProducer> CurrentProducers { get; }

            InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled);
        }

        public IEnumerable<InstanceProducer> CurrentProducers => this.providers.SelectMany(p => p.CurrentProducers);
        private IEnumerable<InstanceProducer> ConditionalProducers => this.CurrentProducers.Where(p => p.IsConditional);
        private IEnumerable<InstanceProducer> UnconditionalProducers => this.CurrentProducers.Where(p => !p.IsConditional);
        public int GetNumberOfConditionalRegistrationsFor(Type serviceType) => this.CurrentProducers.Count(p => p.IsConditional);

        public void Add(InstanceProducer producer)
        {
            this.container.ThrowWhenContainerIsLocked();
            this.ThrowWhenConditionalAndUnconditionalAreMixed(producer);

            this.ThrowWhenTypeAlreadyRegistered(producer);
            this.ThrowWhenProviderToRegisterOverlapsWithExistingProvider(producer);

            if (producer.IsUnconditional)
            {
                this.providers.Clear();
            }

            this.providers.Add(new SingleInstanceProducerProvider(producer));
        }

        private void ThrowWhenProviderToRegisterOverlapsWithExistingProvider(
            InstanceProducer producerToRegister)
        {
            // A provider is a superset of the providerToRegister when it can be applied to ALL generic
            // types that the providerToRegister can be applied to as well.
            var overlappingProducers =
                from producer in this.CurrentProducers
                where producer.ImplementationType != null
                where !producer.Registration.WrapsInstanceCreationDelegate
                where !producerToRegister.Registration.WrapsInstanceCreationDelegate
                where producer.ImplementationType == producerToRegister.ImplementationType
                select producer;

            if (overlappingProducers.Any())
            {
                var overlappingProducer = overlappingProducers.FirstOrDefault();

                throw new InvalidOperationException(
                    StringResources.AnOverlappingGenericRegistrationExists(
                        producerToRegister.ServiceType,
                        overlappingProducer.ImplementationType,
                        overlappingProducer.IsConditional,
                        producerToRegister.ImplementationType,
                        producerToRegister.IsConditional));
            }
        }

        public void Add(Type serviceType, Func<TypeFactoryContext, Type> implementationTypeFactory,
            Lifestyle lifestyle, Predicate<PredicateContext> predicate)
        {
            Requires.IsNotNull(predicate, "only support conditional for now");

            this.container.ThrowWhenContainerIsLocked();

            if (this.UnconditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(serviceType));
            }

            this.providers.Add(new ImplementationTypeFactoryInstanceProducerProvider(serviceType,
                implementationTypeFactory, lifestyle, predicate, this.container));
        }

        public InstanceProducer TryGetInstanceProducer(Type serviceType, InjectionConsumerInfo context)
        {
            var instanceProducers = this.GetInstanceProducers(context).ToArray();

            if (instanceProducers.Length <= 1)
            {
                return instanceProducers.FirstOrDefault();
            }

            throw this.ThrowMultipleApplicableRegistrationsFound(instanceProducers);
        }

        public void AddGeneric(Type serviceType, Type implementationType,
            Lifestyle lifestyle, Predicate<PredicateContext> predicate)
        {
            throw new NotSupportedException();
        }

        private IEnumerable<InstanceProducer> GetInstanceProducers(InjectionConsumerInfo consumer)
        {
            bool handled = false;

            foreach (var provider in this.providers)
            {
                InstanceProducer producer = provider.TryGetProducer(consumer, handled);

                if (producer != null)
                {
                    yield return producer;
                    handled = true;
                }
            }
        }

        private void ThrowWhenTypeAlreadyRegistered(InstanceProducer producer)
        {
            if (producer.IsUnconditional && this.providers.Any() &&
                !this.container.Options.AllowOverridingRegistrations)
            {
                throw new InvalidOperationException(StringResources.TypeAlreadyRegistered(this.nonGenericServiceType));
            }
        }

        private ActivationException ThrowMultipleApplicableRegistrationsFound(
            InstanceProducer[] instanceProducers)
        {
            var producersInfo =
                from producer in instanceProducers
                select Tuple.Create(this.nonGenericServiceType, producer.Registration.ImplementationType, producer);

            return new ActivationException(
                StringResources.MultipleApplicableRegistrationsFound(
                    this.nonGenericServiceType, producersInfo.ToArray()));
        }

        private void ThrowWhenConditionalAndUnconditionalAreMixed(InstanceProducer producer)
        {
            this.ThrowWhenNonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(producer);
            this.ThrowWhenNonGenericTypeAlreadyRegisteredAsConditionalRegistration(producer);
        }

        private void ThrowWhenNonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(
            InstanceProducer producer)
        {
            if (producer.IsConditional && this.UnconditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsUnconditionalRegistration(
                        producer.ServiceType));
            }
        }

        private void ThrowWhenNonGenericTypeAlreadyRegisteredAsConditionalRegistration(
            InstanceProducer producer)
        {
            if (producer.IsUnconditional && this.ConditionalProducers.Any())
            {
                throw new InvalidOperationException(
                    StringResources.NonGenericTypeAlreadyRegisteredAsConditionalRegistration(
                        producer.ServiceType));
            }
        }

        private sealed class SingleInstanceProducerProvider : IProducerProvider
        {
            private readonly InstanceProducer producer;

            public SingleInstanceProducerProvider(InstanceProducer producer)
            {
                this.producer = producer;
            }

            public IEnumerable<InstanceProducer> CurrentProducers => Enumerable.Repeat(this.producer, 1);

            public InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled) =>
                producer.Predicate(new PredicateContext(producer, consumer, handled))
                    ? this.producer
                    : null;
        }

        internal class ImplementationTypeFactoryInstanceProducerProvider : IProducerProvider
        {
            private readonly Dictionary<Type, InstanceProducer> cache = new Dictionary<Type, InstanceProducer>();
            private readonly Func<TypeFactoryContext, Type> implementationTypeFactory;
            private readonly Lifestyle lifestyle;
            private readonly Predicate<PredicateContext> predicate;
            private readonly Type serviceType;
            private readonly Container container;

            public ImplementationTypeFactoryInstanceProducerProvider(Type serviceType,
                Func<TypeFactoryContext, Type> implementationTypeFactory, Lifestyle lifestyle,
                Predicate<PredicateContext> predicate, Container container)
            {
                this.serviceType = serviceType;
                this.implementationTypeFactory = implementationTypeFactory;
                this.lifestyle = lifestyle;
                this.predicate = predicate;
                this.container = container;
            }

            public IEnumerable<InstanceProducer> CurrentProducers
            {
                get
                {
                    lock (this.cache)
                    {
                        return this.cache.Values.ToArray();
                    }
                }
            }

            public InstanceProducer TryGetProducer(InjectionConsumerInfo consumer, bool handled)
            {
                Func<Type> implementationTypeProvider = () =>
                    this.GetImplementationTypeThroughFactory(serviceType, consumer);

                var context = new PredicateContext(serviceType, implementationTypeProvider, consumer, handled);

                // NOTE: The producer should only get built after it matches the delegate, to prevent
                // unneeded producers from being created, because this might cause diagnostic warnings, 
                // such as torn lifestyle warnings.
                return this.predicate(context)
                    ? this.GetProducer(serviceType, context.ImplementationType)
                    : null;
            }

            private Type GetImplementationTypeThroughFactory(Type serviceType, InjectionConsumerInfo consumer)
            {
                Type implementationType =
                    this.implementationTypeFactory(new TypeFactoryContext(serviceType, consumer));

                if (implementationType == null)
                {
                    throw new InvalidOperationException(StringResources.FactoryReturnedNull(this.serviceType));
                }

                if (implementationType.ContainsGenericParameters)
                {
                    throw new ActivationException(
                        StringResources.TheTypeReturnedFromTheFactoryShouldNotBeOpenGeneric(
                            serviceType, implementationType));
                }

                Requires.FactoryReturnsATypeThatIsAssignableFromServiceType(serviceType, implementationType);

                return implementationType;
            }

            private InstanceProducer GetProducer(Type serviceType, Type closedImplementation)
            {
                InstanceProducer producer;

                // Never build a producer twice. This could cause components with a torn lifestyle.
                lock (this.cache)
                {
                    if (!this.cache.TryGetValue(serviceType, out producer))
                    {
                        this.cache[serviceType] =
                            producer = this.CreateNewProducerFor(serviceType, closedImplementation);
                    }
                }

                return producer;
            }

            private InstanceProducer CreateNewProducerFor(Type serviceType, Type closedImplementation) =>
                new InstanceProducer(
                    serviceType,
                    this.lifestyle.CreateRegistration(serviceType, closedImplementation, this.container),
                    this.predicate);
        }
    }
}