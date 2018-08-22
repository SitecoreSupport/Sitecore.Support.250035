namespace Sitecore.Support.XA.Foundation.Search.Providers.Azure
{
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Globalization;
  using System.Linq;
  using System.Xml;
  using Sitecore.ContentSearch;
  using Sitecore.ContentSearch.Azure;
  using Sitecore.ContentSearch.Azure.Http;
  using Sitecore.ContentSearch.Converters;
  using Sitecore.Diagnostics;
  using Sitecore.Exceptions;
  using Sitecore.XA.Foundation.Search.Providers.Azure.Geospatial;
  public class CloudIndexFieldStorageValueFormatter : IndexFieldStorageValueFormatter, ISearchIndexInitializable
  {
    private ICloudSearchIndex _searchIndex;

    public CloudIndexFieldStorageValueFormatter()
    {
      EnumerableConverter = new IndexFieldEnumerableConverter(this);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object,System.Object)", Justification = "This is exact copy of the platform code")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object[])", Justification = "This is exact copy of the platform code")]
    public override object FormatValueForIndexStorage(object value, string fieldName)
    {
      Assert.IsNotNullOrEmpty(fieldName, nameof(fieldName));

      var result = value;

      if (result == null)
      {
        return null;
      }

      var fieldSchema = _searchIndex.SchemaBuilder.GetSchema().GetFieldByCloudName(fieldName);

      #region Added code
      if (fieldSchema == null)
      {
        fieldSchema = this._searchIndex.SearchService.Schema.GetFieldByCloudName(fieldName);
      }
      #endregion

      if (fieldSchema == null)
      {
        return value;
      }

      var cloudTypeMapper = _searchIndex.CloudConfiguration.CloudTypeMapper;
      //This is the only SXA change in this class. Rest is exactly the same like in Sitecore.ContentSearch.Azure.Converters.CloudIndexFieldStorageValueFormatter.
      var fieldType = fieldSchema.Type.ToLower(CultureInfo.InvariantCulture) == "edm.geographypoint" ? typeof(GeoPoint) : cloudTypeMapper.GetNativeType(fieldSchema.Type);

      var context = new IndexFieldConverterContext(fieldName);

      try
      {
        if (result is IIndexableId)
        {
          result = FormatValueForIndexStorage(((IIndexableId)result).Value, fieldName);
        }
        else if (result is IIndexableUniqueId)
        {
          result = FormatValueForIndexStorage(((IIndexableUniqueId)result).Value, fieldName);
        }
        else
        {
          result = ConvertToType(result, fieldType, context);
        }

        if (result != null && !(result is string || fieldType.IsInstanceOfType(result) || (result is IEnumerable<string> && typeof(IEnumerable<string>).IsAssignableFrom(fieldType))))
        {
          throw new InvalidCastException($"Converted value has type '{result.GetType()}', but '{fieldType}' is expected.");
        }
      }
      catch (Exception ex)
      {
        throw new NotSupportedException($"Field '{fieldName}' with value '{value}' of type '{value.GetType()}' cannot be converted to type '{fieldType}' declared for the field in the schema.", ex);
      }

      return result;
    }

    public override object ReadFromIndexStorage(object indexValue, string fieldName, Type destinationType)
    {
      if (indexValue == null)
      {
        return null;
      }

      if (destinationType == null)
      {
        throw new ArgumentNullException("destinationType");
      }

      if (indexValue.GetType() == destinationType)
      {
        return indexValue;
      }

      if (indexValue is IEnumerable && !(indexValue is string))
      {
        var resultList = (indexValue as IEnumerable).Cast<object>().ToArray();

        if (resultList.Length == 0)
        {
          return null;
        }

        if (resultList.Length == 1)
        {
          return ReadFromIndexStorageBase(resultList[0], fieldName, destinationType);
        }
      }

      var result = ReadFromIndexStorageBase(indexValue, fieldName, destinationType);

      if (result == null && destinationType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(destinationType))
      {
        if (destinationType.IsInterface)
        {
          return Activator.CreateInstance(typeof(List<>).MakeGenericType(destinationType.GetGenericArguments()));
        }

        return Activator.CreateInstance(destinationType);
      }

      return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object)", Justification = "This is exact copy of the platform code")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object,System.Object)", Justification = "This is exact copy of the platform code")]
    public override void AddConverter(XmlNode configNode)
    {
      var handlerTypeAttribute = configNode.Attributes["handlesType"];
      if (handlerTypeAttribute == null)
      {
        throw new ConfigurationException("Attribute 'handlesType' is required.");
      }

      var converterTypeAttribute = configNode.Attributes["typeConverter"];
      if (converterTypeAttribute == null)
      {
        throw new ConfigurationException("Attribute 'typeConverter' is required.");
      }

      var handledTypeName = handlerTypeAttribute.Value;
      var converterTypeName = configNode.Attributes["typeConverter"].Value;

      var converterNode = new XmlDocument();
      if (configNode.HasChildNodes)
      {
        converterNode.LoadXml($"<converter type=\"{converterTypeName}\">{configNode.InnerXml}</converter>");
      }
      else
      {
        converterNode.LoadXml($"<converter type=\"{converterTypeName}\" />");
      }

      var handledType = Type.GetType(handledTypeName);
      var factory = _searchIndex?.Locator?.GetInstance<IFactoryWrapper>() ?? new FactoryWrapper();
      var converter = factory.CreateObject<TypeConverter>(converterNode.DocumentElement, true);

      AddConverter(handledType, converter);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object)", Justification = "This is exact copy of the platform code")]
    public new void Initialize(ISearchIndex searchIndex)
    {
      var index = searchIndex as ICloudSearchIndex;

      #region Change for compatibility with C# 6
     
      if (index == null)
      {
        throw new NotSupportedException($"Only {typeof(CloudSearchProviderIndex).Name} is supported");
      }
      this._searchIndex = index;
      #endregion

      base.Initialize(searchIndex);
    }

    private TypeConverter GetConverter(Type type)
    {
      var result = Converters.GetTypeConverter(type);

      if (result == null)
      {
        var interfaces = type.GetInterfaces();

        foreach (var @interface in interfaces)
        {
          result = Converters.GetTypeConverter(@interface);
          if (result != null)
          {
            return result;
          }
        }
      }

      return result;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object,System.Object,System.Object)", Justification = "This is exact copy of the platform code")]
    private object ConvertToType(object value, Type expectedType, ITypeDescriptorContext context)
    {
      object result;

      var valueType = value.GetType();

      if (valueType == expectedType)
      {
        return value;
      }

      if (typeof(IEnumerable<string>).IsAssignableFrom(valueType) && typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        return value;
      }

      var typeConverter = GetConverter(value.GetType());

      if (typeConverter != null && typeConverter.CanConvertTo(context, expectedType))
      {
        return typeConverter.ConvertTo(context, CultureInfo.CurrentCulture, value, expectedType);
      }

      if (TryConvertToPrimitiveType(value, expectedType, context, out result))
      {
        return result;
      }

      if (TryConvertToEnumerable(value, expectedType, context, out result))
      {
        return result;
      }

      throw new InvalidCastException($"Cannon cast value '{value}' of type '{value.GetType()}' to '{expectedType}'.");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.Convert.ChangeType(System.Object,System.Type)", Justification = "This is exact copy of the platform code")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo", MessageId = "Sitecore.DateUtil.ParseDateTime(System.String,System.DateTime)", Justification = "This is exact copy of the platform code")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.DateTimeOffset.Parse(System.String)", Justification = "This is exact copy of the platform code")]
    private bool TryConvertToPrimitiveType(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (value as string == string.Empty && expectedType.IsValueType)
      {
        result = Activator.CreateInstance(expectedType);

        return true;
      }

      if (value is string)
      {
        var valueStr = (string)value;

        if (expectedType == typeof(bool))
        {
          if (valueStr == "1")
          {
            result = true;

            return true;
          }

          if (valueStr == "0" || valueStr == string.Empty)
          {
            result = false;

            return true;
          }
        }
        else if (expectedType == typeof(DateTimeOffset))
        {
          if (valueStr.Length > 15 && (int)valueStr[15] == 58)
          {
            result = (DateTimeOffset)DateUtil.ParseDateTime(valueStr, DateTime.MinValue);
          }
          else
          {
            result = DateTimeOffset.Parse(valueStr);
          }

          return true;
        }
      }

      if (value is IConvertible)
      {
        if (expectedType == typeof(bool)
            || expectedType == typeof(string)
            || expectedType == typeof(int)
            || expectedType == typeof(long)
            || expectedType == typeof(double)
            || expectedType == typeof(float))
        {
          result = System.Convert.ChangeType(value, expectedType);

          return true;
        }
      }

      result = null;

      return false;
    }

    private bool TryConvertToEnumerable(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        if (value is string || !(value is IEnumerable))
        {
          var convertedItem = ConvertToType(value, typeof(string), context);

          if (!(convertedItem is string))
          {
            result = null;

            return false;
          }

          result = new[]
          {
                        (string)convertedItem
                    };

          return true;
        }

        if (!(value is IEnumerable<string>))
        {
          var list = new List<string>();

          foreach (var item in (IEnumerable)value)
          {
            var convertedItem = ConvertToType(item, typeof(string), context);

            if (!(convertedItem is string))
            {
              result = null;

              return false;
            }

            list.Add((string)convertedItem);
          }

          result = list.ToArray();

          return true;
        }
      }

      result = null;

      return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.String.Format(System.String,System.Object,System.Object,System.Object)", Justification = "This is exact copy of the platform code")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider", MessageId = "System.Convert.ChangeType(System.Object,System.Type)", Justification = "This is exact copy of the platform code")]
    private object ReadFromIndexStorageBase(object indexValue, string fieldName, Type destinationType)
    {
      if (indexValue == null)
      {
        return null;
      }

      if (destinationType == null)
      {
        throw new ArgumentNullException("destinationType");
      }

      if (destinationType.IsInstanceOfType(indexValue))
      {
        return indexValue;
      }

      try
      {
        var converterContext = new IndexFieldConverterContext(fieldName);
        var converter = Converters.GetTypeConverter(destinationType);

        // Convert to enumerable type?
        if (EnumerableConverter != null && EnumerableConverter.CanConvertTo(destinationType))
        {
          if (indexValue is IEnumerable && indexValue.GetType() != typeof(string))
          {
            return EnumerableConverter.ConvertTo(converterContext, CultureInfo.InvariantCulture, indexValue, destinationType);
          }

          // Convert single value to an enumerable with a single element
          if (destinationType != typeof(string) && !indexValue.Equals(string.Empty))
          {
            return EnumerableConverter.ConvertTo(converterContext, CultureInfo.InvariantCulture, new[] { indexValue }, destinationType);
          }
        }

        if (typeof(IConvertible).IsAssignableFrom(destinationType) && !indexValue.Equals(string.Empty))
        {
          return System.Convert.ChangeType(indexValue, destinationType);
        }

        return converter?.ConvertFrom(converterContext, CultureInfo.InvariantCulture, indexValue);
      }
      catch (InvalidCastException ex)
      {
        throw new InvalidCastException($"Could not convert value of type {indexValue.GetType().FullName} to destination type {destinationType.FullName}: {ex.Message}", ex);
      }
    }
  }
}