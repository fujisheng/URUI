using System;
using System.Globalization;
namespace RmlUnity {
 public static class RmlNumericCompatibility {
  // Sufficient exact-representation condition, not approximate rounding.
  internal static bool TryExactFixed(float value,string format,IFormatProvider provider,out float result){
   result=value;
   if(!ReferenceEquals(provider,CultureInfo.InvariantCulture)&&!ReferenceEquals(provider,NumberFormatInfo.InvariantInfo))return false;
   if(string.IsNullOrEmpty(format)||format[0]!='0')return false;
   int d=0;if(format.Length!=1){if(format.Length<3||format.Length>8||format[1]!='.')return false;d=format.Length-2;for(int i=2;i<format.Length;i++)if(format[i]!='0'&&format[i]!='#')return false;}
   int bits=BitConverter.SingleToInt32Bits(value);if(bits==int.MinValue||(bits&0x7f800000)==0x7f800000)return false;
   float limit;switch(d){case 0:limit=1000000;break;case 1:limit=100000;break;case 2:limit=10000;break;case 3:limit=1000;break;case 4:limit=100;break;case 5:limit=10;break;default:limit=1;break;}
   if(Math.Abs(value)>=limit)return false;
   float scaled=value*(1<<d);return scaled==(int)scaled;
  }
  /// <summary>Opt-in exact fixed-format fast path. All other inputs retain the
  /// existing codec, including negative zero, nonfinite and custom providers.</summary>
  public static float FormatRoundTrip(float value,string format,IFormatProvider provider,bool exactFixedRepresentation){
   if(exactFixedRepresentation&&TryExactFixed(value,format,provider,out float result))return result;
   return FormatRoundTrip(value,format,provider);
  }
  /// <summary>
  /// Compatibility with float.Parse(value.ToString(format, provider), provider),
  /// without an intermediate string on the supported small-format path.
  /// Numeric Rml APIs already accept floats directly; this codec is only for
  /// callers retaining an existing text representation's precision contract.
  /// </summary>
  public static float FormatRoundTrip(float value,string format,IFormatProvider provider){
   // Custom/stateful providers retain the exact legacy call sequence. Nonfinite
   // symbols and unusually large outputs also retain the established fallback.
   if(float.IsNaN(value)||float.IsInfinity(value)||(provider!=null&&provider.GetType()!=typeof(CultureInfo)&&provider.GetType()!=typeof(NumberFormatInfo)))return float.Parse(value.ToString(format,provider),provider);
   Span<char> chars=stackalloc char[128];
   if(value.TryFormat(chars,out int written,format,provider))return float.Parse(chars.Slice(0,written),NumberStyles.Float|NumberStyles.AllowThousands,provider);
   return float.Parse(value.ToString(format,provider),provider);
  }
 }
}
