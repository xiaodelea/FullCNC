﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows;

using System.Numerics;
using System.Diagnostics;

using ControllerCNC.Machine;
using ControllerCNC.Primitives;

namespace ControllerCNC.Planning
{
    /// <summary>
    /// Simple planner for constant speed transitions between 2D coordinates.
    /// </summary>
    class StraightLinePlanner4D
    {
        /// <summary>
        /// The desired speed of transition between points.
        /// </summary>
        private readonly Speed _transitionSpeed;


        public StraightLinePlanner4D(Speed transitionSpeed)
        {
            _transitionSpeed = transitionSpeed;
        }

        /// <summary>
        /// Creates the plan connecting coordinates constant speed without acceleration.
        /// </summary>
        /// <param name="trajectory">Trajectory which plan will be created.</param>
        /// <returns>The created plan.</returns>
        public PlanBuilder CreateConstantPlan(Trajectory4D trajectory, IEnumerable<Speed> segmentSpeeds = null)
        {
            if (segmentSpeeds == null)
                segmentSpeeds = new Speed[0];

            var speeds = new Queue<Speed>(segmentSpeeds);

            var planBuilder = new PlanBuilder();
            iterateDistances(trajectory, (p, u, v, x, y) =>
            {
                var speed = _transitionSpeed;
                if (speeds.Any())
                    speed = speeds.Dequeue();

                planBuilder.AddConstantSpeedTransitionUVXY(u, v, speed, x, y, speed);
            });
            return planBuilder;
        }

        /// <summary>
        /// Creates the plan connecting coordinates by ramped lines.
        /// </summary>
        /// <param name="trajectory">Trajectory which plan will be created.</param>
        /// <returns>The created plan.</returns>
        public PlanBuilder CreateRampedPlan(Trajectory4D trajectory)
        {
            var planBuilder = new PlanBuilder();

            iterateDistances(trajectory, (p, u, v, x, y) => planBuilder.AddRampedLineUVXY(u, v, x, y, Constants.MaxPlaneAcceleration, Constants.MaxPlaneSpeed));
            return planBuilder;
        }

        private void iterateDistances(Trajectory4D trajectory, Action<Point4Dstep, int, int, int, int> planner)
        {
            Point4Dstep lastPoint = null;
            foreach (var point in trajectory.Points)
            {
                if (lastPoint == null)
                {
                    lastPoint = point;
                    continue;
                }

                var distanceU = point.U - lastPoint.U;
                var distanceV = point.V - lastPoint.V;
                var distanceX = point.X - lastPoint.X;
                var distanceY = point.Y - lastPoint.Y;

                planner(point, distanceU, distanceV, distanceX, distanceY);
                lastPoint = point;
            }
        }
    }
}
