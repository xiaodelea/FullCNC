﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Drawing;
using System.Drawing.Imaging;

using ControllerCNC.Primitives;

namespace ControllerCNC.Planning
{
    /// <summary>
    /// Finds coordinate interpolation of image against background. (specified by (0,0) point)
    /// </summary>
    public class ImageInterpolator
    {
        private BitmapMash _mash;

        internal readonly double ShrinkThreshold = 0.75;

        public ImageInterpolator(string filename)
        {
            var image = new Bitmap(Image.FromFile(filename));
            var bitmapData = image.LockBits(new Rectangle(new Point(), image.Size), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

            var ptr = bitmapData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            var dataSize = Math.Abs(bitmapData.Stride) * image.Height;
            var data = new byte[dataSize];
            var stride = bitmapData.Stride;
            var width = bitmapData.Width;
            var height = bitmapData.Height;
            System.Runtime.InteropServices.Marshal.Copy(ptr, data, 0, dataSize);
            image.UnlockBits(bitmapData);

            _mash = new BitmapMash(width, height, data, stride);
        }

        public IEnumerable<Point2Dmm> InterpolatePoints()
        {
            var parts = _mash.GetShapeParts();
            var shape = joinParts(parts);
            var shrinkedShape = shrinkLines(shape);
            return shrinkedShape;
        }

        public IEnumerable<Point2Dmm[]> InterpolatePointsIsolated()
        {
            var parts = _mash.GetShapeParts();
            var shrinkedShapes = parts.Select(p => shrinkLines(p.Concat(new[] { p.First() })).ToArray());
            return shrinkedShapes;
        }

        public static IEnumerable<Point2Dmm[]> FlattenStokes(IEnumerable<Point2Dmm[]> parts)
        {
            var flatteningTolerance = 1.0;
            var partsArr = parts.Select(filter).ToArray();
            var updates = new List<Tuple<int, int, int, int>>();
            for (var i = 0; i < partsArr.Length; ++i)
            {
                for (var j = 0; j < partsArr[i].Length - 1; ++j)
                {
                    var n = getNearestWithOpositeSpin(i, j, partsArr);
                    if (n == null)
                        continue;

                    var distance = getDistance(i, j, n.Item1, n.Item2, partsArr);

                    if (distance < flatteningTolerance)
                    {
                        var update = Tuple.Create(i, j, n.Item1, n.Item2);
                        updates.Add(update);
                    }
                }
            }

            foreach (var update in updates)
            {
                var p1 = partsArr[update.Item1][update.Item2];
                var p2 = partsArr[update.Item3][update.Item4];
                var v = p2 - p1;
                var np = p1 + v / 2;

                var c1 = partsArr[update.Item1];
                var c2 = partsArr[update.Item3];
                c1[update.Item2] = np;
                c2[update.Item4] = np;
                if (update.Item2 == 0)
                {
                    c1[c1.Length - 1] = np;
                }

                if (update.Item4 == 0)
                {
                    c2[c2.Length - 1] = np;
                }
            }

            var result = partsArr.Select(c => filter(c).ToArray()).Select(c => c.Select(OffsetCalculator.AsPoint2D).ToArray());
            return result;
        }

        private static System.Windows.Point[] filter(Point2Dmm[] arg)
        {
            var result = new List<System.Windows.Point>();
            foreach (var point in arg)
            {
                var p = OffsetCalculator.AsPoint(point);
                if (result.Count > 0 && (result.Last() - p).Length < 0.1)
                    continue;

                result.Add(p);
            }

            return result.ToArray();
        }

        private static System.Windows.Point[] filter(System.Windows.Point[] arg)
        {
            var result = new List<System.Windows.Point>();
            foreach (var p in arg)
            {
                if (result.Count > 0 && (result.Last() - p).Length < 0.1)
                    continue;

                result.Add(p);
            }

            return result.ToArray();
        }

        private static double getDistance(int i1, int j1, int i2, int j2, System.Windows.Point[][] partsArr)
        {
            var p1 = partsArr[i1][j1];
            var p2 = partsArr[i2][j2];

            return (p2 - p1).Length;
        }

        private static Tuple<int, int> getNearestWithOpositeSpin(int i, int j, System.Windows.Point[][] partsArr)
        {
            var clusterStart = j + 1;
            var bestDistance = double.PositiveInfinity;
            Tuple<int, int> bestOpositeSpine = null;
            var spin1 = getSpin(i, j, partsArr);
            for (var i2 = i; i2 < partsArr.Length; ++i2)
            {
                for (var j2 = clusterStart; j2 < partsArr[i2].Length - 1; ++j2)
                {
                    var distance = getDistance(i, j, i2, j2, partsArr);

                    var spin2 = getSpin(i2, j2, partsArr);
                    if (isOposite(spin1, spin2) == (i != i2))
                        continue;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestOpositeSpine = Tuple.Create(i2, j2);
                    }
                }

                clusterStart = 0;
            }

            return bestOpositeSpine;
        }

        private static bool isOposite(System.Windows.Vector spin1, System.Windows.Vector spin2)
        {
            var angle = (System.Windows.Vector.AngleBetween(spin1, spin2));

            if (Math.Abs(angle) > 180)
                throw new NotImplementedException();

            return Math.Abs(angle) > 170;
        }

        private static System.Windows.Vector getSpin(int i, int j, System.Windows.Point[][] partsArr)
        {
            var clLen = partsArr[i].Length;
            var previousPoint = partsArr[i][(j - 1 + clLen) % clLen];
            var nextPoint = partsArr[i][(j + 1) % clLen];

            return nextPoint - previousPoint;
        }



        private IEnumerable<Point2Dmm> joinParts(IEnumerable<Point2Dmm[]> parts)
        {
            var closedParts = new List<Point2Dmm[]>();
            foreach (var part in parts)
            {
                //for joining we need closed parts
                var closedPart = part.Concat(new[] { part[0] }).ToArray();
                closedParts.Add(closedPart);
            }

            //fill table of join length between shapes
            var joinLengthSqr = new double[closedParts.Count, closedParts.Count];
            var joinStart = new Point2Dmm[closedParts.Count, closedParts.Count];
            var joinEnd = new Point2Dmm[closedParts.Count, closedParts.Count];

            for (var shape1Index = 0; shape1Index < closedParts.Count - 1; ++shape1Index)
            {
                joinLengthSqr[shape1Index, shape1Index] = double.PositiveInfinity;
                var shape1 = closedParts[shape1Index];
                for (var shape2Index = shape1Index + 1; shape2Index < closedParts.Count; ++shape2Index)
                {
                    var shape2 = closedParts[shape2Index];

                    joinLengthSqr[shape2Index, shape1Index] = double.PositiveInfinity;
                    joinLengthSqr[shape1Index, shape2Index] = double.PositiveInfinity;
                    for (var shape1PointIndex = 0; shape1PointIndex < shape1.Length; ++shape1PointIndex)
                    {
                        for (var shape2PointIndex = 0; shape2PointIndex < shape2.Length; ++shape2PointIndex)
                        {
                            var shape1Point = shape1[shape1PointIndex];
                            var shape2Point = shape2[shape2PointIndex];

                            var diffX = shape1Point.C1 - shape2Point.C1;
                            var diffY = shape1Point.C2 - shape2Point.C2;
                            var lengthSqr = 1.0 * diffX * diffX + diffY * diffY;
                            if (lengthSqr < joinLengthSqr[shape1Index, shape2Index])
                            {
                                joinLengthSqr[shape1Index, shape2Index] = lengthSqr;
                                joinStart[shape1Index, shape2Index] = shape1Point;
                                joinEnd[shape1Index, shape2Index] = shape2Point;
                            }
                        }
                    }
                }
            }

            //join shapes by the shortest joins

            for (var i = 0; i < closedParts.Count - 1; ++i)
            {
                //n shapes will have n-1 joins
                var bestShape1Index = 0;
                var bestShape2Index = 0;
                for (var shape1Index = 0; shape1Index < closedParts.Count - 1; ++shape1Index)
                {
                    for (var shape2Index = shape1Index + 1; shape2Index < closedParts.Count; ++shape2Index)
                    {
                        var shape1 = closedParts[shape1Index];
                        var shape2 = closedParts[shape2Index];
                        if (shape1 == shape2)
                            //shapes are already joined
                            continue;

                        if (joinLengthSqr[shape1Index, shape2Index] < joinLengthSqr[bestShape1Index, bestShape2Index])
                        {
                            bestShape1Index = shape1Index;
                            bestShape2Index = shape2Index;
                        }
                    }
                }

                var shape1Point = joinStart[bestShape1Index, bestShape2Index];
                var shape2Point = joinEnd[bestShape1Index, bestShape2Index];
                joinShapes(closedParts, bestShape1Index, shape1Point, bestShape2Index, shape2Point);
                joinLengthSqr[bestShape1Index, bestShape2Index] = double.NaN;
            }

            //all slots should contain same shape
            return closedParts[0];
        }

        private void joinShapes(List<Point2Dmm[]> shapes, int shape1Index, Point2Dmm shape1Point, int shape2Index, Point2Dmm shape2Point)
        {
            var shape1 = shapes[shape1Index];
            var shape2 = shapes[shape2Index];
            for (var i = 0; i < shape1.Length; ++i)
            {
                var point = shape1[i];
                if (point != shape1Point)
                    continue;

                var reorderedShape2 = reorderClosedShapeTo(shape2, shape2Point);


                var newShape = shape1.Take(i + 1).Concat(reorderedShape2).Concat(new[] { reorderedShape2.First() }).Concat(shape1.Skip(i)).ToArray();
                var targetShape1 = shapes[shape1Index];
                var targetShape2 = shapes[shape2Index];
                for (var j = 0; j < shapes.Count; ++j)
                {
                    if (shapes[j] == targetShape1 || shapes[j] == targetShape2)
                        shapes[j] = newShape;
                }
                return;
            }

            throw new InvalidOperationException("Join was not successful");
        }

        private Point2Dmm[] reorderClosedShapeTo(Point2Dmm[] shape, Point2Dmm startPoint)
        {
            var startIndex = -1;
            for (var i = 0; i < shape.Length; ++i)
            {
                if (shape[i] == startPoint)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex < 0)
                throw new NotSupportedException("Startpoint not found");

            var result = new List<Point2Dmm>();
            for (var i = startIndex; i < shape.Length + startIndex + 1; ++i)
            {
                result.Add(shape[i % shape.Length]);
            }

            return result.ToArray();
        }

        private IEnumerable<Point2Dmm> shrinkLines(IEnumerable<Point2Dmm> points)
        {
            var result = new List<Point2Dmm>();
            result.Add(points.First());

            var lastPointIndex = 0;
            var pointsArray = points.ToArray();
            for (var i = 1; i < pointsArray.Length - 1; ++i)
            {
                var lastPoint = pointsArray[lastPointIndex];
                var point = pointsArray[i];
                var nextPoint = pointsArray[i + 1];
                var totalDiffX = nextPoint.C1 - lastPoint.C1;
                var totalDiffY = nextPoint.C2 - lastPoint.C2;

                var lineLength = Math.Sqrt(totalDiffX * totalDiffX + totalDiffY * totalDiffY);

                var ratioX = 1.0 * totalDiffX / lineLength;
                var ratioY = 1.0 * totalDiffY / lineLength;

                var totalLinePoints = i + 1 - lastPointIndex;
                var isLineGoodAproximation = true;
                for (var j = lastPointIndex + 1; j < lastPointIndex + totalLinePoints; ++j)
                {
                    //check whether line is a good aproximator
                    var estimatedPoint = pointsArray[j];

                    var currentLinePoints = j - lastPointIndex;
                    var estimationLength = lineLength * currentLinePoints / totalLinePoints;
                    var approxX = lastPoint.C1 + ratioX * estimationLength;
                    var approxY = lastPoint.C2 + ratioY * estimationLength;


                    var diffX = (approxX - estimatedPoint.C1);
                    var diffY = (approxY - estimatedPoint.C2);

                    //if (Math.Sqrt(diffX * diffX + diffY * diffY) > threshold)
                    if (Math.Abs(diffX) > ShrinkThreshold || Math.Abs(diffY) > ShrinkThreshold)
                    {
                        isLineGoodAproximation = false;
                        break;
                    }
                }

                if (isLineGoodAproximation)
                    //point can be skipped (it will be approximated by the line)
                    continue;

                lastPointIndex = i;
                result.Add(point);
            }

            result.Add(points.Last());

            return result;
        }
    }
}
