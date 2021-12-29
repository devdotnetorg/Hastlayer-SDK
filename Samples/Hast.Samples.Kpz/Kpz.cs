using Hast.Layer;
using Hast.Samples.Kpz.Algorithms;
using Lombiq.HelpfulLibraries.Libraries.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Hast.Samples.Kpz
{
    public enum KpzTarget
    {
        Cpu,
        Fpga,
        FpgaSimulation,
        FpgaParallelized,
        FpgaSimulationParallelized,
        PrngTest,
    }

    public static class KpzTargetExtensions
    {
        public static bool HastlayerSimulation(this KpzTarget target) =>
            target is KpzTarget.FpgaSimulation or KpzTarget.FpgaSimulationParallelized;

        public static bool HastlayerOnFpga(this KpzTarget target) =>
            target is KpzTarget.Fpga or KpzTarget.FpgaParallelized or KpzTarget.PrngTest;

        public static bool HastlayerParallelizedAlgorithm(this KpzTarget target) =>
            target is KpzTarget.FpgaParallelized or KpzTarget.FpgaSimulationParallelized;

        public static bool HastlayerPlainAlgorithm(this KpzTarget target) =>
            target is KpzTarget.Fpga or KpzTarget.FpgaSimulation;
    }

    /// <summary>
    /// This class performs the calculations of the KPZ algorithm.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S106:Standard outputs should not be used directly to log anything", Justification = "Opt-in via setting.")]
    [SuppressMessage("Minor Vulnerability", "S2228:Console logging should not be used", Justification = "Opt-in via setting.")]
    public partial class Kpz
    {
        /// <summary>The probability of pyramid to hole change.</summary>
        private readonly double _probabilityP;

        /// <summary>The probability of hole to pyramid change.</summary>
        private readonly double _probabilityQ;

        /// <summary>The pseudorandom generator is used at various places in the algorithm.</summary>
        private readonly NonSecurityRandomizer _random = new();

        /// <summary>See <see cref="StateLogger" />.</summary>
        private readonly bool _enableStateLogger;

        private readonly KpzTarget _kpzTarget;

        /// <summary>
        /// Gets the width of the grid.
        /// </summary>
        public int GridWidth => Grid.GetLength(0);

        /// <summary>
        /// Gets the height of the grid.
        /// </summary>
        public int GridHeight => Grid.GetLength(1);

#pragma warning disable CA1819 // Properties should not return arrays
        /// <summary>
        /// Gets or sets the 2D grid of <see cref="KpzNode" /> items on which the KPZ algorithm is performed.
        /// </summary>
        public KpzNode[,] Grid { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

        /// <summary>
        /// <para>Gets or sets the <see cref="StateLogger" /> (if enabled) allows us to inspect the state of the algorithm at
        /// given steps during its execution. This object can be later passed on to <see cref="InspectForm" />
        /// to graphically represent it on a UI.</para>
        /// <note type="caution">
        /// <para>Use a small grid and a low amount of iterations if enabled. It will use a lot of memory.</para>
        /// </note>
        /// </summary>
        public KpzStateLogger StateLogger { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Kpz"/> class.
        /// The constructor initializes the parameters of <see cref="Kpz" />, see:
        /// <see cref="GridWidth" />, <see cref="GridHeight" />,
        /// <see cref="_probabilityP" />, <see cref="_probabilityQ" />,
        /// <see cref="StateLogger" />.
        /// </summary>
        public Kpz(
            int newGridWidth,
            int newGridHeight,
            double probabilityP,
            double probabilityQ,
            bool enableStateLogger,
            KpzTarget target
        )
        {
            Grid = new KpzNode[newGridWidth, newGridHeight];
            _probabilityP = probabilityP;
            _probabilityQ = probabilityQ;
            _enableStateLogger = enableStateLogger;
            if (_enableStateLogger) StateLogger = new KpzStateLogger();
            _kpzTarget = target;
        }

        /// <summary>It fills the <see cref="Grid" /> with random data.</summary>
        public void RandomizeGrid()
        {
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    Grid[x, y] = new KpzNode
                    {
                        // Not crypto.
                        Dx = _random.GetFromRange(2) == 0,
                        Dy = _random.GetFromRange(2) == 0,
                    };
                }
            }

            if (_enableStateLogger) StateLogger.AddKpzAction("RandomizeGrid", this);
        }

        /// <summary>
        /// Fill grid with a pattern that already contains pyramids and holes, so the KPZ algorithm can work on it.
        /// </summary>
        public void InitializeGrid()
        {
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    Grid[x, y] = new KpzNode
                    {
                        Dx = (x & 1) != 0,
                        Dy = (y & 1) != 0,
                    };
                }
            }

            if (_enableStateLogger) StateLogger.AddKpzAction("InitializeGrid", this);
        }

        /// <summary>
        /// It is used during heightmap generation.
        /// It converts <see cref="KpzNode.Dx" /> and <see cref="KpzNode.Dy" /> boolean values to +1 and -1 integer
        /// values.
        /// </summary>
        private static int Bool2Delta(bool what) => what ? 1 : -1;

        public HeightmapMetaData GenerateAndProcessHeightMap()
        {
            var map = GenerateHeightMap(
                out var mean,
                out var periodicityValid,
                out var periodicityInvalidXCount,
                out var periodicityInvalidYCount);

            return new HeightmapMetaData
            {
                Mean = mean,
                PeriodicityInvalidXCount = periodicityInvalidXCount,
                PeriodicityInvalidYCount = periodicityInvalidYCount,
                PeriodicityValid = periodicityValid,
                StandardDeviation = HeightMapStandardDeviation(map, mean),
            };
        }

        /// <summary>
        /// It generates a heightmap from the <see cref="Grid" />.
        /// </summary>
        /// <param name="mean"> output is the mean of the heightmap to be used in statistic calculations later.</param>
        /// <param name="periodicityValid">
        /// output is true if the periodicity of <see cref="Grid" /> is correct at the boundaries.
        /// </param>
        /// <param name="periodicityInvalidXCount">
        /// output is the number of periodicity errors counted at the borders in the X direction
        /// (between left and right borders).
        /// </param>
        /// <param name="periodicityInvalidYCount">
        /// output is the number of periodicity errors counted at the borders in the Y direction
        /// (between top and bottom borders).
        /// </param>
        /// <returns>the heightmap.</returns>
        private int[,] GenerateHeightMap(
            out double mean,
            out bool periodicityValid,
            out int periodicityInvalidXCount,
            out int periodicityInvalidYCount
        )
        {
            const bool doVerboseLoggingToConsole = true;
            int[,] heightMap = new int[GridWidth, GridHeight];
            int heightNow = 0;
            mean = 0;
            periodicityValid = true;
            periodicityInvalidXCount = periodicityInvalidYCount = 0;

            for (int y = 0; y < GridHeight; y++)
            {
                for (int x = 0; x < GridWidth; x++)
                {
                    heightNow += Bool2Delta(Grid[(x + 1) % GridWidth, y].Dx);
                    heightMap[x, y] = heightNow;
                    mean += heightNow;
                }

                if (heightNow + Bool2Delta(Grid[1, y].Dx) != heightMap[0, y])
                {
                    periodicityValid = false;
                    periodicityInvalidXCount++;
                    if (doVerboseLoggingToConsole)
                        Console.WriteLine(FormattableString.Invariant($"periodicityInvalidX at line {y}"));
                }

                heightNow += Bool2Delta(Grid[0, (y + 1) % GridHeight].Dy);
            }

            if (heightMap[0, GridHeight - 1] + Bool2Delta(Grid[0, 0].Dy) != heightMap[0, 0])
            {
                periodicityValid = false;
                periodicityInvalidYCount++;
                if (doVerboseLoggingToConsole)
                {
                    Console.Write(FormattableString.Invariant(
                        $"periodicityInvalidY {heightMap[0, GridHeight - 1]} + {Bool2Delta(Grid[0, 0].Dy)} != {heightMap[0, 0]}"));
                }
            }

            if (_enableStateLogger) StateLogger.AddKpzAction("GenerateHeightMap", heightMap);

            mean /= GridWidth * GridHeight;

            return heightMap;
        }

        /// <summary>
        /// It calculates the standard deviation of a heightmap created with <see cref="GenerateHeightMap" />.
        /// The idea behind it is available at
        /// <a href="https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance#Two-pass_algorithm">Wikipedia</a>.
        /// </summary>
        /// <param name="mean">is the mean of the heightmap that was output by <see cref="GenerateHeightMap" />.</param>
        private double HeightMapStandardDeviation(int[,] inputHeightMap, double mean)
        {
            double variance = 0;

            for (int y = 0; y < GridHeight; y++)
            {
                for (int x = 0; x < GridWidth; x++)
                {
                    variance += Math.Pow(inputHeightMap[x, y] - mean, 2);
                }
            }

            variance /= (GridWidth * GridHeight) - 1;
            double standardDeviation = Math.Sqrt(variance);

            if (_enableStateLogger)
                StateLogger.AddKpzAction($"HeightMapStandardDeviation: {standardDeviation}");

            return standardDeviation;
        }

        /// <summary>
        /// Detects pyramid or hole (if any) at the given coordinates in the <see cref="Grid" />, and randomly switch
        /// between pyramid and hole, based on <see cref="_probabilityP" /> and <see cref="_probabilityQ" /> parameters.
        /// </summary>
        /// <param name="p">
        /// contains the coordinates where the function looks if there is a pyramid or hole in the <see cref="Grid" />.
        /// </param>
        private void RandomlySwitchFourCells(KpzNode[,] grid, KpzCoords p)
        {
            var neighbours = GetNeighbours(grid, p);
            var currentPoint = grid[p.X, p.Y];
            bool changedGrid = false;

            // We check our own {dx,dy} values, and the right neighbour's dx, and bottom neighbour's dx.
            if (
                // If we get the pattern {01, 01} we have a pyramid:
                (currentPoint.Dx && !neighbours.Nx.Dx && currentPoint.Dy && !neighbours.Ny.Dy && _random.GetDouble() < _probabilityP) ||
                // If we get the pattern {10, 10} we have a hole:
                (!currentPoint.Dx && neighbours.Nx.Dx && !currentPoint.Dy && neighbours.Ny.Dy && _random.GetDouble() < _probabilityQ)
            )
            {
                // We make a hole into a pyramid, and a pyramid into a hole.
                currentPoint.Dx = !currentPoint.Dx;
                currentPoint.Dy = !currentPoint.Dy;
                neighbours.Nx.Dx = !neighbours.Nx.Dx;
                neighbours.Ny.Dy = !neighbours.Ny.Dy;
                changedGrid = true;
            }

            if (_enableStateLogger) StateLogger.AddKpzAction("RandomlySwitchFourCells", grid, p, neighbours, changedGrid);
        }

        /// <summary>
        /// Runs an iteration of the KPZ algorithm (with <see cref="GridWidth"/> × <see cref="GridHeight"/> steps).
        /// </summary>
        public void DoHastIterations(
            IHastlayer hastlayer,
            IHardwareGenerationConfiguration configuration,
            uint numberOfIterations)
        {
            var gridBefore = (KpzNode[,])Grid.Clone();

            if (_enableStateLogger) StateLogger.NewKpzIteration();

            if (_kpzTarget is KpzTarget.FpgaParallelized or KpzTarget.FpgaSimulationParallelized)
            {
                KernelsParallelized.DoIterationsWrapper(
                    hastlayer,
                    configuration,
                    Grid,
pushToFpga: false,
                    _randomSeedEnable,
                    numberOfIterations);
            }
            else
            {
                Kernels.DoIterationsWrapper(
                    hastlayer,
                    configuration,
                    Grid,
                    pushToFpga: false,
                    testMode: false,
                    _random.NextUInt64(),
                    _random.NextUInt64(),
                    numberOfIterations);
            }

            if (_enableStateLogger) StateLogger.AddKpzAction("Kernels.DoHastIterations", Grid, gridBefore);
            // HastlayerGridAlreadyPushed = true; // If not commented out, push always
        }

        /// <summary>
        /// Runs an iteration of the KPZ algorithm (with <see cref="GridWidth"/> × <see cref="GridHeight"/> steps).
        /// It allows us to debug the steps of the algorithms one by one.
        /// </summary>
        public void DoHastIterationDebug(IHastlayer hastlayer, IHardwareGenerationConfiguration configuration)
        {
            var numberOfStepsInIteration = GridWidth * GridHeight;
            var gridBefore = (KpzNode[,])Grid.Clone();

            if (_enableStateLogger) StateLogger.NewKpzIteration();

            for (int i = 0; i < numberOfStepsInIteration; i++)
            {
                Kernels.DoIterationsWrapper(
                    hastlayer,
                    configuration,
                    Grid,
                    pushToFpga: true,
                    testMode: true,
                    _random.NextUInt64(),
                    _random.NextUInt64(),
                    numberOfIterations: 1);
                if (_enableStateLogger) StateLogger.AddKpzAction("Kernels.DoSingleIterationWrapper", Grid, gridBefore);
            }
        }

        /// <summary>
        /// Runs an iteration of the KPZ algorithm (with <see cref="GridWidth"/> × <see cref="GridHeight"/> steps).
        /// </summary>
        public void DoIteration()
        {
            var numberOfStepsInIteration = GridWidth * GridHeight;

            if (_enableStateLogger) StateLogger.NewKpzIteration();

            for (int i = 0; i < numberOfStepsInIteration; i++)
            {
                // We randomly choose a point on the grid.
                // If there is a pyramid or hole, we randomly switch them.

                // Not crypto.
                var randomPoint = new KpzCoords { X = _random.GetFromRange(GridWidth), Y = _random.GetFromRange(GridHeight) };
                RandomlySwitchFourCells(Grid, randomPoint);
            }
        }

        /// <summary>
        /// Gets the right and bottom neighbours of the point given with the coordinates <paramref name="p"/>
        /// in the <see cref="Grid" />.
        /// </summary>
        private KpzNeighbours GetNeighbours(KpzNode[,] grid, KpzCoords p)
        {
            var toReturn = new KpzNeighbours
            {
                NxCoords = new KpzCoords
                {
                    X = p.X < GridWidth - 1 ? p.X + 1 : 0,
                    Y = p.Y,
                },

                NyCoords = new KpzCoords
                {
                    X = p.X,
                    Y = p.Y < GridHeight - 1 ? p.Y + 1 : 0,
                },
            };

            toReturn.Nx = grid[toReturn.NxCoords.X, toReturn.NxCoords.Y];
            toReturn.Ny = grid[toReturn.NyCoords.X, toReturn.NyCoords.Y];

            return toReturn;
        }
    }

    /// <summary>
    /// This class extends the <see cref="NonSecurityRandomizer"/> class with convenience functions.
    /// </summary>
    internal static class RandomExtensions
    {
        public static ulong NextUInt64(this NonSecurityRandomizer random)
        {
            ulong val1 = (uint)random.Get();
            ulong val2 = (uint)random.Get();

            return val1 | (val2 << 32);
        }
    }
}
