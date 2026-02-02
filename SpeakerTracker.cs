#nullable enable
using System;
using System.Collections.Generic;

namespace GhostBar
{
    /// <summary>
    /// Tracks speakers using x-vector cosine distance comparison.
    /// Assigns persistent labels like "Speaker 1", "Speaker 2", etc.
    /// </summary>
    public class SpeakerTracker
    {
        private readonly List<SpeakerProfile> _knownSpeakers = new();
        private readonly double _similarityThreshold;
        private readonly object _lock = new();

        /// <summary>
        /// Create a new speaker tracker
        /// </summary>
        /// <param name="similarityThreshold">Cosine distance threshold for same speaker (default 0.5, lower = stricter)</param>
        public SpeakerTracker(double similarityThreshold = 0.5)
        {
            _similarityThreshold = similarityThreshold;
        }

        /// <summary>
        /// Identify or register a speaker based on their voice vector
        /// </summary>
        /// <param name="speakerVector">128-dimensional x-vector from Vosk</param>
        /// <returns>Speaker ID like "Speaker 1", "Speaker 2", etc.</returns>
        public string IdentifySpeaker(double[] speakerVector)
        {
            if (speakerVector == null || speakerVector.Length == 0)
            {
                return "Unknown";
            }

            lock (_lock)
            {
                // Find the closest matching known speaker
                SpeakerProfile? bestMatch = null;
                double bestDistance = double.MaxValue;

                foreach (var speaker in _knownSpeakers)
                {
                    var distance = CosineDistance(speakerVector, speaker.ReferenceVector);
                    
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestMatch = speaker;
                    }
                }

                // If we found a close enough match, return that speaker
                if (bestMatch != null && bestDistance < _similarityThreshold)
                {
                    // Update the reference vector with a weighted average for better accuracy over time
                    bestMatch.UpdateVector(speakerVector);
                    bestMatch.SegmentCount++;
                    
                    Logger.Info($"Matched {bestMatch.Id} (distance: {bestDistance:F3})");
                    return bestMatch.Id;
                }

                // No match found - register new speaker
                var newSpeakerId = $"Speaker {_knownSpeakers.Count + 1}";
                var newSpeaker = new SpeakerProfile
                {
                    Id = newSpeakerId,
                    ReferenceVector = (double[])speakerVector.Clone(),
                    SegmentCount = 1
                };
                _knownSpeakers.Add(newSpeaker);

                Logger.Info($"Registered new speaker: {newSpeakerId}");
                return newSpeakerId;
            }
        }

        /// <summary>
        /// Get the number of unique speakers detected
        /// </summary>
        public int SpeakerCount
        {
            get
            {
                lock (_lock)
                {
                    return _knownSpeakers.Count;
                }
            }
        }

        /// <summary>
        /// Get all known speaker IDs
        /// </summary>
        public List<string> GetSpeakerIds()
        {
            lock (_lock)
            {
                var ids = new List<string>();
                foreach (var speaker in _knownSpeakers)
                {
                    ids.Add(speaker.Id);
                }
                return ids;
            }
        }

        /// <summary>
        /// Reset all speaker tracking
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _knownSpeakers.Clear();
                Logger.Info("SpeakerTracker reset");
            }
        }

        /// <summary>
        /// Calculate cosine distance between two vectors (0 = identical, 2 = opposite)
        /// </summary>
        private static double CosineDistance(double[] x, double[] y)
        {
            if (x.Length != y.Length)
            {
                throw new ArgumentException("Vectors must have the same length");
            }

            double dot = 0, normX = 0, normY = 0;
            
            for (int i = 0; i < x.Length; i++)
            {
                dot += x[i] * y[i];
                normX += x[i] * x[i];
                normY += y[i] * y[i];
            }

            if (normX == 0 || normY == 0)
            {
                return 1.0; // Maximum distance if either vector is zero
            }

            var cosineSimilarity = dot / (Math.Sqrt(normX) * Math.Sqrt(normY));
            return 1.0 - cosineSimilarity; // Convert similarity to distance
        }

        /// <summary>
        /// Internal speaker profile
        /// </summary>
        private class SpeakerProfile
        {
            public string Id { get; set; } = "";
            public double[] ReferenceVector { get; set; } = Array.Empty<double>();
            public int SegmentCount { get; set; }

            /// <summary>
            /// Update reference vector with exponential moving average
            /// </summary>
            public void UpdateVector(double[] newVector)
            {
                if (ReferenceVector.Length != newVector.Length)
                    return;

                // Use exponential moving average with alpha = 0.3
                const double alpha = 0.3;
                
                for (int i = 0; i < ReferenceVector.Length; i++)
                {
                    ReferenceVector[i] = (1 - alpha) * ReferenceVector[i] + alpha * newVector[i];
                }
            }
        }
    }
}
