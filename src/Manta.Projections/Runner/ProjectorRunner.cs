﻿using System;
using System.Collections.Generic;

namespace Manta.Projections.Runner
{
    public class ProjectorRunner : IDisposable
    {
        private readonly List<ProjectorRunnerContext> _projectors;

        public ProjectorRunner()
        {
            _projectors = new List<ProjectorRunnerContext>();
        }

        public void Add(ProjectorBase projector, TimeSpan? runForDuration = null)
        {
            _projectors.Add(new ProjectorRunnerContext(projector, runForDuration));
        }

        public void Start()
        {
            foreach (var projector in _projectors)
            {
                projector.Start();
            }
        }

        public void Stop()
        {
            foreach (var projector in _projectors)
            {
                projector.Stop();
            }
        }

        public void Dispose()
        {
            foreach (var projector in _projectors)
            {
                projector.Dispose();
            }
        }
    }
}