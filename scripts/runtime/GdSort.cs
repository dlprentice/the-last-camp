namespace GdRuntime;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>Godot's SortArray (core/templates/sort_array.h): introsort with median-of-3 pivots, a heap-sort fallback
/// and a final insertion sort. Array.sort() and sort_custom() order equal elements by exactly this algorithm, so code
/// that sorts with ties keeps GDScript's resulting order.</summary>
public static class GdSort
{
    private const int IntrosortThreshold = 16;

    public static void Sort<T>(IList<T> list, Func<T, T, bool> less)
    {
        int n = list.Count;
        if (n < 2)
            return;
        T[] a = new T[n];
        list.CopyTo(a, 0);
        new Sorter<T>(less).SortRange(0, n, a);
        if (list is List<T> l)
        {
            for (int i = 0; i < n; i++)
                l[i] = a[i];
            return;
        }
        for (int i = 0; i < n; i++)
            list[i] = a[i];
    }

    private readonly struct Sorter<T>
    {
        private readonly Func<T, T, bool> _less;

        public Sorter(Func<T, T, bool> less) => _less = less;

        private T MedianOf3(T a, T b, T c)
        {
            if (_less(a, b))
            {
                if (_less(b, c))
                    return b;
                if (_less(a, c))
                    return c;
                return a;
            }
            if (_less(a, c))
                return a;
            if (_less(b, c))
                return c;
            return b;
        }

        private static long Bitlog(long n)
        {
            long k;
            for (k = 0; n != 1; n >>= 1)
                ++k;
            return k;
        }

        private void PushHeap(long first, long holeIdx, long topIndex, T value, T[] a)
        {
            long parent = (holeIdx - 1) / 2;
            while (holeIdx > topIndex && _less(a[first + parent], value))
            {
                a[first + holeIdx] = a[first + parent];
                holeIdx = parent;
                parent = (holeIdx - 1) / 2;
            }
            a[first + holeIdx] = value;
        }

        private void PopHeap(long first, long last, long result, T value, T[] a)
        {
            a[result] = a[first];
            AdjustHeap(first, 0, last - first, value, a);
        }

        private void PopHeap(long first, long last, T[] a) => PopHeap(first, last - 1, last - 1, a[last - 1], a);

        private void AdjustHeap(long first, long holeIdx, long len, T value, T[] a)
        {
            long topIndex = holeIdx;
            long secondChild = 2 * holeIdx + 2;
            while (secondChild < len)
            {
                if (_less(a[first + secondChild], a[first + (secondChild - 1)]))
                    secondChild--;
                a[first + holeIdx] = a[first + secondChild];
                holeIdx = secondChild;
                secondChild = 2 * (secondChild + 1);
            }
            if (secondChild == len)
            {
                a[first + holeIdx] = a[first + (secondChild - 1)];
                holeIdx = secondChild - 1;
            }
            PushHeap(first, holeIdx, topIndex, value, a);
        }

        private void SortHeap(long first, long last, T[] a)
        {
            while (last - first > 1)
                PopHeap(first, last--, a);
        }

        private void MakeHeap(long first, long last, T[] a)
        {
            if (last - first < 2)
                return;
            long len = last - first;
            long parent = (len - 2) / 2;
            while (true)
            {
                AdjustHeap(first, parent, len, a[first + parent], a);
                if (parent == 0)
                    return;
                parent--;
            }
        }

        private void PartialSort(long first, long last, long middle, T[] a)
        {
            MakeHeap(first, middle, a);
            for (long i = middle; i < last; i++)
                if (_less(a[i], a[first]))
                    PopHeap(first, middle, i, a[i], a);
            SortHeap(first, middle, a);
        }

        private long Partitioner(long first, long last, T pivot, T[] a)
        {
            long unmodifiedFirst = first;
            long unmodifiedLast = last;
            while (true)
            {
                while (_less(a[first], pivot))
                {
                    if (first == unmodifiedLast - 1)
                    {
                        GD.PrintErr("bad comparison function; sorting will be broken");
                        break;
                    }
                    first++;
                }
                last--;
                while (_less(pivot, a[last]))
                {
                    if (last == unmodifiedFirst)
                    {
                        GD.PrintErr("bad comparison function; sorting will be broken");
                        break;
                    }
                    last--;
                }
                if (!(first < last))
                    return first;
                (a[first], a[last]) = (a[last], a[first]);
                first++;
            }
        }

        private void Introsort(long first, long last, T[] a, long maxDepth)
        {
            while (last - first > IntrosortThreshold)
            {
                if (maxDepth == 0)
                {
                    PartialSort(first, last, last, a);
                    return;
                }
                maxDepth--;
                long cut = Partitioner(first, last, MedianOf3(a[first], a[first + (last - first) / 2], a[last - 1]), a);
                Introsort(cut, last, a, maxDepth);
                last = cut;
            }
        }

        private void UnguardedLinearInsert(long last, T value, T[] a)
        {
            long next = last - 1;
            while (_less(value, a[next]))
            {
                if (next == 0)
                {
                    GD.PrintErr("bad comparison function; sorting will be broken");
                    break;
                }
                a[last] = a[next];
                last = next;
                next--;
            }
            a[last] = value;
        }

        private void LinearInsert(long first, long last, T[] a)
        {
            T val = a[last];
            if (_less(val, a[first]))
            {
                for (long i = last; i > first; i--)
                    a[i] = a[i - 1];
                a[first] = val;
            }
            else
            {
                UnguardedLinearInsert(last, val, a);
            }
        }

        private void InsertionSort(long first, long last, T[] a)
        {
            if (first == last)
                return;
            for (long i = first + 1; i != last; i++)
                LinearInsert(first, i, a);
        }

        private void UnguardedInsertionSort(long first, long last, T[] a)
        {
            for (long i = first; i != last; i++)
                UnguardedLinearInsert(i, a[i], a);
        }

        private void FinalInsertionSort(long first, long last, T[] a)
        {
            if (last - first > IntrosortThreshold)
            {
                InsertionSort(first, first + IntrosortThreshold, a);
                UnguardedInsertionSort(first + IntrosortThreshold, last, a);
            }
            else
            {
                InsertionSort(first, last, a);
            }
        }

        public void SortRange(long first, long last, T[] a)
        {
            if (first != last)
            {
                Introsort(first, last, a, Bitlog(last - first) * 2);
                FinalInsertionSort(first, last, a);
            }
        }
    }
}
