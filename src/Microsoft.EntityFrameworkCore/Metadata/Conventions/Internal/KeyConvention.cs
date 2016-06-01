// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class KeyConvention
        : IKeyConvention, IPrimaryKeyConvention, IForeignKeyConvention, IForeignKeyRemovedConvention, IModelConvention
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalKeyBuilder Apply(InternalKeyBuilder keyBuilder)
        {
            SetValueGeneration(keyBuilder.Metadata.Properties);

            return keyBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalRelationshipBuilder Apply(InternalRelationshipBuilder relationshipBuilder)
        {
            foreach (var property in relationshipBuilder.Metadata.Properties)
            {
                var propertyBuilder = property.Builder;
                propertyBuilder.RequiresValueGenerator(false, ConfigurationSource.Convention);
                propertyBuilder.ValueGenerated(ValueGenerated.Never, ConfigurationSource.Convention);
            }

            return relationshipBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Apply(InternalEntityTypeBuilder entityTypeBuilder, ForeignKey foreignKey)
        {
            var properties = foreignKey.Properties;
            SetValueGeneration(properties.Where(property => property.IsKey()));
            SetIdentity(properties, entityTypeBuilder.Metadata);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual bool Apply(InternalKeyBuilder keyBuilder, Key previousPrimaryKey)
        {
            if (previousPrimaryKey != null)
            {
                foreach (var property in previousPrimaryKey.Properties)
                {
                    property.Builder?.ValueGenerated(ValueGenerated.Never, ConfigurationSource.Convention);
                    property.Builder?.RequiresValueGenerator(false, ConfigurationSource.Convention);
                }
            }

            SetIdentity(keyBuilder.Metadata.Properties, keyBuilder.Metadata.DeclaringEntityType);

            return true;
        }

        private static void SetValueGeneration(IEnumerable<Property> properties)
        {
            var generatingProperties = properties.Where(property =>
                !property.IsForeignKey()
                && property.ValueGenerated == ValueGenerated.OnAdd);
            foreach (var propertyBuilder in generatingProperties)
            {
                propertyBuilder.Builder?.RequiresValueGenerator(true, ConfigurationSource.Convention);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual Property FindValueGeneratedOnAddProperty(
            [NotNull] IReadOnlyList<Property> properties, [NotNull] EntityType entityType)
        {
            Check.NotNull(properties, nameof(properties));
            Check.NotNull(entityType, nameof(entityType));

            if (entityType.FindPrimaryKey(properties) != null
                && properties.Count == 1)
            {
                var property = properties.First();
                if (!property.IsForeignKey())
                {
                    var propertyType = property.ClrType.UnwrapNullableType();
                    if (propertyType.IsInteger()
                        || propertyType == typeof(Guid))
                    {
                        return property;
                    }
                }
            }
            return null;
        }

        private void SetIdentity(IReadOnlyList<Property> properties, EntityType entityType)
        {
            var candidateIdentityProperty = FindValueGeneratedOnAddProperty(properties, entityType);
            if (candidateIdentityProperty != null)
            {
                var propertyBuilder = candidateIdentityProperty.Builder;
                propertyBuilder?.ValueGenerated(ValueGenerated.OnAdd, ConfigurationSource.Convention);
                propertyBuilder?.RequiresValueGenerator(true, ConfigurationSource.Convention);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual InternalModelBuilder Apply(InternalModelBuilder modelBuilder)
        {
            var messages = GetShadowKeyExceptionMessage(
                modelBuilder.Metadata,
                key => key.Properties.Any(p => p.IsShadowProperty
                                               && ConfigurationSource.Convention.Overrides(((Property)p).GetConfigurationSource())));
            if (messages != null
                && messages.Any())
            {
                throw new InvalidOperationException(messages.First());
            }

            return modelBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used 
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static List<string> GetShadowKeyExceptionMessage([NotNull] IModel model, [NotNull] Func<IKey, bool> keyPredicate)
        {
            List<string> messages = null;
            foreach (var entityType in model.GetEntityTypes())
            {
                foreach (var key in entityType.GetDeclaredKeys())
                {
                    if (keyPredicate(key))
                    {
                        string message;
                        var referencingFk = key.GetReferencingForeignKeys().FirstOrDefault();
                        if (referencingFk != null)
                        {
                            if (referencingFk.DependentToPrincipal == null
                                && referencingFk.PrincipalToDependent == null)
                            {
                                message = CoreStrings.ReferencedShadowKeyWithoutNavigations(
                                    Property.Format(key.Properties),
                                    entityType.DisplayName(),
                                    Property.Format(referencingFk.Properties),
                                    referencingFk.DeclaringEntityType.DisplayName());
                            }
                            else
                            {
                                message = CoreStrings.ReferencedShadowKey(
                                    Property.Format(key.Properties),
                                    entityType.DisplayName() +
                                    (referencingFk.PrincipalToDependent == null
                                        ? ""
                                        : "." + referencingFk.PrincipalToDependent.Name),
                                    referencingFk.DeclaringEntityType.DisplayName() +
                                    (referencingFk.DependentToPrincipal == null
                                        ? ""
                                        : "." + referencingFk.DependentToPrincipal.Name));
                            }
                        }
                        else
                        {
                            message = CoreStrings.ShadowKey(
                                Property.Format(key.Properties),
                                entityType.DisplayName(),
                                Property.Format(key.Properties));
                        }

                        messages = messages ?? new List<string>();
                        messages.Add(message);
                    }
                }
            }

            return messages;
        }
    }
}
