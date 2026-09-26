#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using Environment = Godot.Environment;
using Range = Godot.Range;
using LastCamp;
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

/// Minimal base class for headless unit tests.
///
/// Subclasses define methods whose names start with [code]test_[/code]; [method run]
/// discovers and executes them in alphabetical order. Assertions never abort a test,
/// they only record a message in [member failures], so one test can report several
/// problems at once.
public partial class TestCase : RefCounted
{
    public Godot.Collections.Array<string> failures = new Godot.Collections.Array<string>();

    public string _current_test = "";

    public void assert_true(bool condition, string message)
    {
        if (!condition)
        {
            _fail(message);
        }
    }

    public void assert_false(bool condition, string message)
    {
        if (condition)
        {
            _fail(message);
        }
    }

    public void assert_eq(Variant a, Variant b, string message)
    {
        if (!G.eq(a, b))
        {
            _fail(G.format("%s (expected %s, got %s)", new Godot.Collections.Array { message, G.str(b), G.str(a) }));
        }
    }

    public void assert_near(double a, double b, double tolerance, string message)
    {
        if (!is_finite(a) || !is_finite(b) || absf(a - b) > tolerance)
        {
            _fail(G.format("%s (expected %s within %s, got %s)", new Godot.Collections.Array { message, G.str(b), G.str(tolerance), G.str(a) }));
        }
    }

    public void assert_gt(double a, double b, string message)
    {
        if (!(a > b))
        {
            _fail(G.format("%s (expected %s > %s)", new Godot.Collections.Array { message, G.str(a), G.str(b) }));
        }
    }

    public void assert_lt(double a, double b, string message)
    {
        if (!(a < b))
        {
            _fail(G.format("%s (expected %s < %s)", new Godot.Collections.Array { message, G.str(a), G.str(b) }));
        }
    }

    public Godot.Collections.Dictionary run()
    {
        /// Runs every [code]test_*[/code] method and returns
        /// [code]{passed: int, failed: int, failures: Array[String]}[/code].
        Godot.Collections.Array<string> names = new Godot.Collections.Array<string>();
        foreach (Godot.Collections.Dictionary method in GetMethodList())
        {
            string method_name = method["name"].AsString();
            if (method_name.StartsWith("test_", StringComparison.Ordinal) && !names.Contains(method_name))
            {
                names.Add(method_name);
            }
        }
        G.sort(names);

        long passed = 0;
        long failed = 0;
        foreach (string method_name2 in names)
        {
            long failures_before = (long)failures.Count;
            _current_test = method_name2;
            try
            {
                GetType().GetMethod(method_name2)!.Invoke(this, null);
            }
            catch (System.Reflection.TargetInvocationException error)
            {
                _fail(error.InnerException?.ToString() ?? error.ToString());
            }
            _current_test = "";
            if ((long)failures.Count == failures_before)
            {
                passed += 1;
            }
            else
            {
                failed += 1;
            }
        }
        return new Godot.Collections.Dictionary { { "passed", passed }, { "failed", failed }, { "failures", failures.Duplicate() } };
    }

    public void _fail(string message)
    {
        string prefix = !(_current_test.Length == 0) ? _current_test + ": " : "";
        failures.Add(prefix + message);
    }
}
