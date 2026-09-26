namespace GdRuntime;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

/// <summary>GDScript's global functions with the engine's semantics, for code translated from GDScript. Floats
/// are doubles and integers 64-bit, as in GDScript; each formula mirrors Godot 4.8's C++ (rounding, signed zero and
/// NaN handling included). Printing, str(), string formatting, randomness and hashing call the engine itself.
/// Names keep GDScript's spelling so translated code reads like its source.</summary>
public static partial class G
{
    public const double PI = 3.1415926535897932384626433833;
    public const double TAU = 6.2831853071795864769252867666;
    public const double INF = double.PositiveInfinity;
    /// <summary>GDScript's NAN is the positive quiet NaN; C#'s double.NaN has the sign bit set.</summary>
    public static readonly double NAN = BitConverter.Int64BitsToDouble(0x7FF8000000000000);
    private const double CMP_EPSILON = 0.00001;

    // -- numbers
    public static double maxf(double a, double b) => a > b ? a : b;
    public static double minf(double a, double b) => a < b ? a : b;
    public static double clampf(double value, double min, double max) => value < min ? min : (value > max ? max : value);
    public static long maxi(long a, long b) => a > b ? a : b;
    public static long mini(long a, long b) => a < b ? a : b;
    public static long clampi(long value, long min, long max) => value < min ? min : (value > max ? max : value);
    /// <summary>C's fabs: clears the sign bit, NaN included (.NET's Math.Abs returns a negative NaN unchanged).</summary>
    /// <summary>The engine's absf: a NaN keeps its sign (measured through the engine's utility functions).</summary>
    public static double absf(double value) => double.IsNaN(value) ? value : Math.Abs(value);
    public static long absi(long value) => value < 0 ? -value : value;
    public static double abs(double value) => absf(value);
    public static long abs(long value) => value < 0 ? -value : value;
    public static Vector2 abs(Vector2 value) => value.Abs();
    public static Vector3 abs(Vector3 value) => value.Abs();
    public static Vector2I abs(Vector2I value) => value.Abs();
    public static Vector3I abs(Vector3I value) => value.Abs();
    /// <summary>Godot 4's SIGN macro: positive 1, negative -1, anything else (zero, NaN) 0.</summary>
    public static double signf(double value) => value > 0.0 ? 1.0 : (value < 0.0 ? -1.0 : 0.0);
    public static long signi(long value) => value > 0 ? 1 : (value < 0 ? -1 : 0);
    public static double sign(double value) => signf(value);
    public static long sign(long value) => signi(value);
    public static Vector3 sign(Vector3 value) => value.Sign();
    public static Vector2 sign(Vector2 value) => value.Sign();
    public static double min(double a, double b) => a < b ? a : b;
    public static long min(long a, long b) => a < b ? a : b;
    public static double max(double a, double b) => a > b ? a : b;
    public static long max(long a, long b) => a > b ? a : b;
    public static double clamp(double value, double min, double max) => clampf(value, min, max);
    public static long clamp(long value, long min, long max) => clampi(value, min, max);
    public static Vector3 clamp(Vector3 value, Vector3 min, Vector3 max) => value.Clamp(min, max);
    public static Vector2 clamp(Vector2 value, Vector2 min, Vector2 max) => value.Clamp(min, max);
    public static double floor(double value) => Math.Floor(value);
    public static double floorf(double value) => Math.Floor(value);
    public static long floori(double value) => (long)Math.Floor(value);
    public static Vector3 floor(Vector3 value) => value.Floor();
    public static Vector2 floor(Vector2 value) => value.Floor();
    public static double ceil(double value) => Math.Ceiling(value);
    public static double ceilf(double value) => Math.Ceiling(value);
    public static long ceili(double value) => (long)Math.Ceiling(value);
    public static Vector3 ceil(Vector3 value) => value.Ceil();
    public static Vector2 ceil(Vector2 value) => value.Ceil();
    public static double round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    public static double roundf(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    public static long roundi(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    public static Vector3 round(Vector3 value) => value.Round();
    public static Vector2 round(Vector2 value) => value.Round();
    public static double sqrt(double value) => Math.Sqrt(value);
    public static double sin(double value) => Math.Sin(value);
    public static double cos(double value) => Math.Cos(value);
    public static double tan(double value) => Math.Tan(value);
    public static double sinh(double value) => Math.Sinh(value);
    public static double cosh(double value) => Math.Cosh(value);
    public static double tanh(double value) => Math.Tanh(value);
    /// <summary>Godot clamps asin/acos inputs outside [-1, 1] instead of returning NaN.</summary>
    public static double asin(double value) => value < -1.0 ? -PI / 2.0 : (value > 1.0 ? PI / 2.0 : Math.Asin(value));
    public static double acos(double value) => value < -1.0 ? PI : (value > 1.0 ? 0.0 : Math.Acos(value));
    public static double atan(double value) => Math.Atan(value);
    public static double atan2(double y, double x) => Math.Atan2(y, x);
    public static double exp(double value) => Math.Exp(value);
    public static double log(double value) => Math.Log(value);
    public static double pow(double x, double y) => Math.Pow(x, y);
    /// <summary>C's fmod; a NaN operand comes back unchanged, the first one when both are NaN.</summary>
    /// <summary>The engine's fmod: a positive NaN operand propagates first, then the NaN operand (measured through
    /// the engine's utility functions); otherwise the truncated remainder.</summary>
    public static double fmod(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            if (double.IsNaN(a) && !double.IsNegative(a))
                return a;
            if (double.IsNaN(b) && !double.IsNegative(b))
                return b;
            return double.IsNaN(a) ? a : b;
        }
        return a % b;
    }
    public static double fposmod(double x, double y)
    {
        double value = fmod(x, y);
        if ((value < 0.0 && y > 0.0) || (value > 0.0 && y < 0.0))
            value += y;
        return value + 0.0;
    }
    public static long posmod(long x, long y)
    {
        if (y == 0)
        {
            GD.PushError("Division by zero in posmod is undefined. Returning 0 as fallback.");
            return 0;
        }
        long value = x % y;
        if ((value < 0 && y > 0) || (value > 0 && y < 0))
            value += y;
        return value;
    }
    public static bool is_nan(double value) => double.IsNaN(value);
    public static bool is_inf(double value) => double.IsInfinity(value);
    public static bool is_finite(double value) => double.IsFinite(value);
    public static bool is_equal_approx(double a, double b)
    {
        if (a == b)
            return true;
        double tolerance = CMP_EPSILON * Math.Abs(a);
        if (tolerance < CMP_EPSILON)
            tolerance = CMP_EPSILON;
        return Math.Abs(a - b) < tolerance;
    }
    public static bool is_zero_approx(double value) => Math.Abs(value) < CMP_EPSILON;
    public static double deg_to_rad(double degrees) => degrees * (PI / 180.0);
    public static double rad_to_deg(double radians) => radians * (180.0 / PI);
    public static double lerpf(double from, double to, double weight) => from + (to - from) * weight;
    public static double lerp(double from, double to, double weight) => from + (to - from) * weight;
    public static Vector2 lerp(Vector2 from, Vector2 to, float weight) => from.Lerp(to, weight);
    public static Vector3 lerp(Vector3 from, Vector3 to, float weight) => from.Lerp(to, weight);
    public static Vector4 lerp(Vector4 from, Vector4 to, float weight) => from.Lerp(to, weight);
    public static Color lerp(Color from, Color to, float weight) => from.Lerp(to, weight);
    public static Quaternion lerp(Quaternion from, Quaternion to, float weight) => from.Slerp(to, weight);
    public static Basis lerp(Basis from, Basis to, float weight) => from.Slerp(to, weight);
    public static Transform3D lerp(Transform3D from, Transform3D to, float weight) => from.InterpolateWith(to, weight);
    public static double inverse_lerp(double from, double to, double weight) => (weight - from) / (to - from);
    public static double remap(double value, double istart, double istop, double ostart, double ostop) =>
        lerpf(ostart, ostop, inverse_lerp(istart, istop, value));
    public static double angle_difference(double from, double to)
    {
        double difference = fmod(to - from, TAU);
        return fmod(2.0 * difference, TAU) - difference;
    }
    public static double lerp_angle(double from, double to, double weight) => from + angle_difference(from, to) * weight;
    public static double smoothstep(double from, double to, double s)
    {
        if (is_equal_approx(from, to))
        {
            if (from <= to)
                return s <= from ? 0.0 : 1.0;
            return s <= to ? 1.0 : 0.0;
        }
        double t = clampf((s - from) / (to - from), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
    public static double move_toward(double from, double to, double delta) =>
        Math.Abs(to - from) <= delta ? to : from + signf(to - from) * delta;
    public static double snappedf(double value, double step) => step != 0.0 ? Math.Floor(value / step + 0.5) * step : value;
    public static double snapped(double value, double step) => snappedf(value, step);
    public static long snapped(long value, long step) => snappedi(value, step);
    public static Vector3 snapped(Vector3 value, Vector3 step) => value.Snapped(step);
    public static Vector2 snapped(Vector2 value, Vector2 step) => value.Snapped(step);
    public static long snappedi(double value, long step) => step != 0 ? (long)(Math.Floor(value / step + 0.5) * step) : (long)value;
    public static double wrapf(double value, double min, double max)
    {
        double range = max - min;
        if (is_zero_approx(range))
            return min;
        double result = value - (range * Math.Floor((value - min) / range));
        if (is_equal_approx(result, max))
            return min;
        return result;
    }
    public static long wrapi(long value, long min, long max)
    {
        long range = max - min;
        return range == 0 ? min : min + ((((value - min) % range) + range) % range);
    }
    public static double wrap(double value, double min, double max) => wrapf(value, min, max);
    public static long wrap(long value, long min, long max) => wrapi(value, min, max);
    public static double linear_to_db(double linear) => Math.Log(linear) * 8.6858896380650365530225783783321;
    public static double db_to_linear(double db) => Math.Exp(db * 0.11512925464970228420089957273422);
    public static double ease(double x, double curve) => Mathf.Ease(x, curve);
    public static double pingpong(double value, double length) => Mathf.PingPong(value, length);
    public static long nearest_po2(long value) => Mathf.NearestPo2((int)value);
    public static long step_decimals(double step) => Mathf.StepDecimals(step);

    // -- engine-backed utilities
    public static double randf() => GD.Randf();
    public static long randi() => GD.Randi();
    public static double randf_range(double from, double to) => GD.RandRange(from, to);
    public static long randi_range(long from, long to) => GD.RandRange((int)from, (int)to);
    public static double randfn(double mean, double deviation) => GD.Randfn(mean, deviation);
    public static void randomize() => GD.Randomize();
    public static void seed(long value) => GD.Seed(unchecked((ulong)value));
    public static long hash(Variant value) => (uint)GD.Hash(value);
    /// <summary>str(): the engine's own stringify of each value (Variant.ToString calls it), concatenated.</summary>
    public static string str(params Variant[] values)
    {
        if (values.Length == 1)
            return values[0].ToString();
        var builder = new System.Text.StringBuilder();
        foreach (Variant value in values)
            builder.Append(value.ToString());
        return builder.ToString();
    }
    public static void print(params Variant[] values) => GD.Print(str(values));
    public static void prints(params Variant[] values) => GD.Print(join(" ", values));
    public static void printt(params Variant[] values) => GD.Print(join("\t", values));
    public static void printerr(params Variant[] values) => GD.PrintErr(str(values));
    public static void print_rich(params Variant[] values) => GD.PrintRich(str(values));
    public static void push_error(params Variant[] values) => GD.PushError(str(values));
    public static void push_warning(params Variant[] values) => GD.PushWarning(str(values));

    private static string join(string separator, Variant[] values)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(separator);
            builder.Append(values[i].ToString());
        }
        return builder.ToString();
    }
    public static void print_stack() => GD.Print(System.Environment.StackTrace);
    public static long @typeof(Variant value) => (long)value.VariantType;
    public static string var_to_str(Variant value) => GD.VarToStr(value);
    public static Variant str_to_var(string text) => GD.StrToVar(text);
    public static List<byte> var_to_bytes(Variant value) => new(GD.VarToBytes(value));
    public static Variant bytes_to_var(List<byte> bytes) => GD.BytesToVar(bytes.ToArray());
    public static string error_string(long error) => Eval("error_string(a)", error).AsString();
    public static string type_string(long type) => Eval("type_string(a)", type).AsString();
    public static string @char(long code) => char.ConvertFromUtf32((int)code);
    public static bool is_same(Variant a, Variant b) => Eval("is_same(a, b)", a, b).AsBool();
    public static Resource load(string path) => GD.Load(path);

    /// <summary>A GDScript assert: only in debug builds, and the failure stops the caller like a script error.</summary>
    [Conditional("DEBUG")]
    public static void assert(bool condition, string message = "")
    {
        if (condition)
            return;
        string text = message.Length > 0 ? $"Assertion failed: {message}" : "Assertion failed.";
        GD.PushError(text);
        throw new GdAssertionException(text);
    }

    // -- conversions with GDScript's rules
    /// <summary>String.to_int(): leading sign and digits, other characters skipped, stops at '.'.</summary>
    public static long to_int(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        int to = value.IndexOf('.');
        if (to < 0)
            to = value.Length;
        long integer = 0;
        long sign = 1;
        for (int i = 0; i < to; i++)
        {
            char c = value[i];
            if (c >= '0' && c <= '9')
            {
                bool overflow = integer > long.MaxValue / 10 || (integer == long.MaxValue / 10 && ((sign == 1 && c > '7') || (sign == -1 && c > '8')));
                if (overflow)
                {
                    GD.PushError($"Cannot represent {value} as a 64-bit signed integer, since the value is {(sign == 1 ? "too large." : "too small.")}");
                    return sign == 1 ? long.MaxValue : long.MinValue;
                }
                integer = integer * 10 + (c - '0');
            }
            else if (integer == 0 && c == '-')
            {
                sign = -sign;
            }
        }
        return integer * sign;
    }
    public static long to_int(double value) => double.IsNaN(value) ? 0 : (long)value;
    public static long to_int(Variant value) => value.VariantType switch
    {
        Variant.Type.Float => to_int(value.AsDouble()),
        Variant.Type.String => to_int(value.AsString()),
        Variant.Type.StringName => to_int(value.AsString()),
        Variant.Type.Bool => value.AsBool() ? 1 : 0,
        _ => value.AsInt64(),
    };
    public static double to_float(string value) => Eval("a.to_float()", value).AsDouble();
    public static double to_float(Variant value) => value.VariantType switch
    {
        Variant.Type.String or Variant.Type.StringName => to_float(value.AsString()),
        Variant.Type.Bool => value.AsBool() ? 1.0 : 0.0,
        _ => value.AsDouble(),
    };

    /// <summary>GDScript truthiness for values of unknown type.</summary>
    public static bool truthy(Variant value) => value.VariantType switch
    {
        Variant.Type.Nil => false,
        Variant.Type.Bool => value.AsBool(),
        Variant.Type.Int => value.AsInt64() != 0,
        Variant.Type.Float => value.AsDouble() != 0.0,
        Variant.Type.String => value.AsString().Length > 0,
        Variant.Type.Object => GodotObject.IsInstanceValid(value.AsGodotObject()),
        Variant.Type.Array => value.AsGodotArray().Count > 0,
        Variant.Type.Dictionary => value.AsGodotDictionary().Count > 0,
        _ => !Eval("not a", value).AsBool(),
    };
    public static bool truthy(Callable value) => is_valid(value);

    // -- strings
    /// <summary>GDScript's String % operator, evaluated by the engine so every format matches exactly.</summary>
    public static string format(string template, Variant values) => Eval("a % b", template, values).AsString();
    public static string format_dict(string template, Variant values) => Eval("a.format(b)", template, values).AsString();
    /// <summary>String.split(): allow_empty keeps empty fields; maxsplit > 0 stops after that many splits.</summary>
    public static List<string> split(string text, string delimiter, bool allowEmpty = true, long maxsplit = 0)
    {
        var result = new List<string>();
        if (delimiter.Length == 0)
        {
            foreach (char c in text)
                result.Add(c.ToString());
            return result;
        }
        int from = 0;
        while (true)
        {
            int end = text.IndexOf(delimiter, from, StringComparison.Ordinal);
            if (end < 0 || (maxsplit > 0 && result.Count >= maxsplit))
                end = text.Length;
            string part = text.Substring(from, end - from);
            if (allowEmpty || part.Length > 0)
                result.Add(part);
            if (end == text.Length)
                break;
            from = end + delimiter.Length;
        }
        return result;
    }

    /// <summary>String.rsplit(): like split, but maxsplit counts from the end.</summary>
    public static List<string> rsplit(string text, string delimiter, bool allowEmpty = true, long maxsplit = 0)
    {
        if (maxsplit <= 0)
            return split(text, delimiter, allowEmpty, 0);
        var result = new List<string>();
        int end = text.Length;
        while (true)
        {
            int start = result.Count < maxsplit ? text.LastIndexOf(delimiter, Math.Max(0, end - 1), StringComparison.Ordinal) : -1;
            if (start >= 0 && start + delimiter.Length > end)
                start = -1;
            string part = start < 0 ? text.Substring(0, end) : text.Substring(start + delimiter.Length, end - start - delimiter.Length);
            if (allowEmpty || part.Length > 0)
                result.Insert(0, part);
            if (start < 0)
                break;
            end = start;
        }
        return result;
    }

    public static string left(string text, long length) => length < 0 ? text.Substring(0, (int)Math.Max(0, text.Length + length)) : text.Substring(0, (int)Math.Min(length, text.Length));
    public static string right(string text, long length) => length < 0 ? text.Substring((int)Math.Min(-length, text.Length)) : text.Substring((int)Math.Max(0, text.Length - length));
    /// <summary>String.substr(from, len = -1): out-of-range starts give "", lengths are clamped.</summary>
    public static string substr(string text, long from, long chars = -1)
    {
        if (chars == -1)
            chars = text.Length - from;
        if (text.Length == 0 || from < 0 || from >= text.Length || chars <= 0)
            return "";
        if (from + chars > text.Length)
            chars = text.Length - from;
        return text.Substring((int)from, (int)chars);
    }
    public static string repeat(string text, long count)
    {
        if (count <= 0 || text.Length == 0)
            return "";
        var builder = new System.Text.StringBuilder(text.Length * (int)count);
        for (long i = 0; i < count; i++)
            builder.Append(text);
        return builder.ToString();
    }
    public static string reverse(string text)
    {
        char[] chars = text.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
    public static string get_string_from_utf8(List<byte> bytes) => System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    public static string get_string_from_ascii(List<byte> bytes) => System.Text.Encoding.ASCII.GetString(bytes.ToArray());

    // -- native Expression API for Variant formatting and dynamic operators; no script resource
    private static readonly System.Threading.ThreadLocal<Dictionary<string, Expression>> Expressions =
        new(() => new Dictionary<string, Expression>(), trackAllValues: true);

    /// <summary>Runs every pending finalizer while the engine is alive. The C# wrapper of a RefCounted object that
    /// became garbage just before quit is otherwise finalized after the engine's C# layer has gone, and its release
    /// aborts the process at exit. Tree scripts call this from _Finalize and the game's main node on leaving the tree.</summary>
    public static void drain_finalizers()
    {
        foreach (var cache in Expressions.Values)
        {
            foreach (var expression in cache.Values) expression.Dispose();
            cache.Clear();
        }
        for (int pass = 0; pass < 2; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>A coroutine started without await, as GDScript starts one. An exception is reported as an error, as
    /// GDScript reports a script error, instead of vanishing with an unobserved task.</summary>
    public static async void start_coroutine(System.Threading.Tasks.Task task)
    {
        try
        {
            await task;
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
        }
    }

    public static Variant Eval(string source, params Variant[] inputs)
    {
        var expressions = Expressions.Value!;
        if (!expressions.TryGetValue(source, out Expression? expression))
        {
            expression = new Expression();
            Error error = expression.Parse(source, Names(inputs.Length));
            if (error != Error.Ok)
                GD.PushError($"GdRuntime expression failed to parse: {source}: {expression.GetErrorText()}");
            expressions[source] = expression;
        }
        Variant result = expression.Execute(new Godot.Collections.Array(inputs), null, false, false);
        if (expression.HasExecuteFailed())
            GD.PushError($"GdRuntime expression failed: {source}: {expression.GetErrorText()}");
        return result;
    }

    private static string[] Names(int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
            names[i] = i < 3 ? ((char)('a' + i)).ToString() : $"x{i}";
        return names;
    }
}

public sealed class GdAssertionException : Exception
{
    public GdAssertionException(string message) : base(message) { }
}
