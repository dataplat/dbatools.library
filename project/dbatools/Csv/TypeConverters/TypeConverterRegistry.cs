using System;
using System.Collections.Generic;

namespace Dataplat.Dbatools.Csv.TypeConverters
{
    /// <summary>
    /// Registry for type converters used during CSV parsing.
    /// Provides automatic type conversion based on target column types.
    /// </summary>
    public sealed class TypeConverterRegistry
    {
        private readonly Dictionary<Type, ITypeConverter> _converters = new Dictionary<Type, ITypeConverter>();

        /// <summary>
        /// Gets the default registry with all built-in converters registered.
        /// </summary>
        public static TypeConverterRegistry Default { get; } = CreateDefault();

        private static TypeConverterRegistry CreateDefault()
        {
            var registry = new TypeConverterRegistry();

            // Register all built-in converters
            registry.Register(GuidConverter.Default);
            registry.Register(BooleanConverter.Default);
            registry.Register(DateTimeConverter.Default);
            registry.Register(Int16Converter.Default);
            registry.Register(Int32Converter.Default);
            registry.Register(Int64Converter.Default);
            registry.Register(SingleConverter.Default);
            registry.Register(DoubleConverter.Default);
            registry.Register(DecimalConverter.Default);
            registry.Register(ByteConverter.Default);
            registry.Register(new StringConverter());

            return registry;
        }

        /// <summary>
        /// Registers a type converter for its target type.
        /// </summary>
        public void Register(ITypeConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            _converters[converter.TargetType] = converter;
        }

        /// <summary>
        /// Registers a type converter for a specific type.
        /// </summary>
        public void Register<T>(ITypeConverter<T> converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            _converters[typeof(T)] = converter;
        }

        /// <summary>
        /// Gets a converter for the specified type.
        /// </summary>
        /// <returns>The converter, or null if no converter is registered for the type.</returns>
        public ITypeConverter GetConverter(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            // Handle nullable types
            Type underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                type = underlyingType;
            }

            _converters.TryGetValue(type, out ITypeConverter converter);
            return converter;
        }

        /// <summary>
        /// Gets a typed converter for the specified type.
        /// </summary>
        public ITypeConverter<T> GetConverter<T>()
        {
            return GetConverter(typeof(T)) as ITypeConverter<T>;
        }

        /// <summary>
        /// Attempts to convert a string value to the specified type.
        /// </summary>
        public bool TryConvert(string value, Type targetType, out object result)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));

            // Handle null/empty for nullable types
            Type underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null && string.IsNullOrEmpty(value))
            {
                result = null;
                return true;
            }

            // Handle string type directly
            if (targetType == typeof(string))
            {
                result = value;
                return true;
            }

            var converter = GetConverter(targetType);
            if (converter != null)
            {
                return converter.TryConvert(value, out result);
            }

            // Fall back to Convert.ChangeType
            try
            {
                result = Convert.ChangeType(value, underlyingType ?? targetType);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        /// <summary>
        /// Creates a new registry with additional custom converters.
        /// </summary>
        public TypeConverterRegistry Clone()
        {
            var clone = new TypeConverterRegistry();
            foreach (var kvp in _converters)
            {
                clone._converters[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }

    /// <summary>
    /// Pass-through converter for string values.
    /// </summary>
    internal sealed class StringConverter : TypeConverterBase<string>
    {
        public override bool TryConvert(string value, out string result)
        {
            result = value;
            return true;
        }
    }
}
