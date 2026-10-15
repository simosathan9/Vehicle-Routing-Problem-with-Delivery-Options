using System.Text.RegularExpressions;

namespace VrdpoProject
{
    // Parses an instance file into an immutable Instance (parsed rows + distance/time matrices).
    // File layout (see Instances/README.txt): line 3 header, customer block from line 6, depot line,
    // location block, option block; blocks are separated by two header lines.
    public static class InstanceReader
    {
        public static Instance Read(string path, Settings settings)
        {
            string[] lines = File.ReadAllLines(path);

            string[] header = lines[3].Split('\t', StringSplitOptions.RemoveEmptyEntries);
            int capacity = int.Parse(header[1]);
            int numbLoc = int.Parse(header[2]);
            int numbCus = int.Parse(header[3]);
            int numbOpt = int.Parse(header[4]);

            var customerRows = new int[numbCus][];
            for (int i = 0; i < numbCus; i++)
            {
                string[] f = Regex.Split(lines[6 + i], @"\t*\s");
                customerRows[i] = new[] { int.Parse(f[0]), int.Parse(f[1]) };
            }

            int[] depotRow = ParseInts(lines[8 + numbCus].Split('\t', StringSplitOptions.RemoveEmptyEntries), 8);

            var locationRows = new int[numbLoc - 1][];
            for (int j = 0; j < numbLoc - 1; j++)
            {
                locationRows[j] = ParseInts(Regex.Split(lines[9 + numbCus + j], @"\t+"), 8);
            }

            var optionRows = new int[numbOpt][];
            for (int m = 0; m < numbOpt; m++)
            {
                optionRows[m] = ParseInts(Regex.Split(lines[9 + numbCus + numbLoc + m], @"\t+"), 6);
            }

            BuildMatrices(depotRow, locationRows, settings, out double[,] distance, out double[,] time);
            return new Instance(path, capacity, depotRow, customerRows, locationRows, optionRows, distance, time);
        }

        private static int[] ParseInts(string[] fields, int count)
        {
            var row = new int[count];
            for (int k = 0; k < count; k++)
            {
                row[k] = int.Parse(fields[k]);
            }
            return row;
        }

        // Upper-triangular storage: cell [i, j - i] holds the value for locations i <= j (ids are positions,
        // depot first). Location.Id must equal the position, which the instance files guarantee.
        private static void BuildMatrices(int[] depotRow, int[][] locationRows, Settings settings, out double[,] distance, out double[,] time)
        {
            int rows = locationRows.Length + 1;
            var xs = new int[rows];
            var ys = new int[rows];
            xs[0] = depotRow[1];
            ys[0] = depotRow[2];
            for (int j = 0; j < locationRows.Length; j++)
            {
                xs[j + 1] = locationRows[j][1];
                ys[j + 1] = locationRows[j][2];
            }

            distance = new double[rows, rows];
            time = new double[rows, rows];
            for (int i = 0; i < rows; i++)
            {
                for (int j = i; j < rows; j++)
                {
                    double dist = Math.Sqrt(Math.Pow(xs[i] - xs[j], 2) + Math.Pow(ys[i] - ys[j], 2));
                    if (settings.IntegerMetric)
                    {
                        time[i, j - i] = (int)Math.Ceiling(10 * dist);
                        distance[i, j - i] = (int)Math.Ceiling(10 * dist);
                    }
                    else
                    {
                        time[i, j - i] = 10 * dist;
                        distance[i, j - i] = dist;
                    }
                }
            }
        }
    }
}
