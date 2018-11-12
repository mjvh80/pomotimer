using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

// NOTE: this is included only for reference, it's not compiled here, run it in a separate console application.

namespace ConsoleApplication35
{
    class Program
    {
        /*
         * This generates a cylindar 3D effect.
         * 
         * 2".                ."
         *  |:."-__ 3 .. ___-"
         *  |... B |
         * 0".A..  |  
         *     "-._|
         *          1
         *          
         * We generate the above triangles, A and B: 0->1->2 (A) and 1->3-2 (B, triangle indices).
         * The order of the indices needs to be anti-clockwise in order to make sure that the faces are 'front'	faces.
         * We map the vertices to texture coordinates by mapping (x, y) of the (x, y, z) to the coordinate space for textures (in range [0,1]).
         * See below, (0,0) is the top left of the texture, (1,1) the bottom right.
         */ 
        static void GenerateMyCoords(Point3DCollection points, PointCollection textureCoords, Int32Collection indices)
        {
            var parts = 10;
            var delta = Math.PI / parts;
            var r = 150; var d = 2.0 * r;

            var index = 0;

            for (var y = -r; y <= r; y += 25)
            {
                for (var alpha = Math.PI; alpha >= 0; alpha -= delta)
                {
                    var x = r * Math.Cos(alpha);
                    var z = r * Math.Sin(alpha);

                    points.Add(new Point3D(x, y, z));


                    /*
                      
                        (0,0)         (1,0)    
                        +------------->
                        |
                        |
                        |
                        \/
                        (0,1)         (1,1)
                     */
                    textureCoords.Add(new Point((x + r) / d, (r - y) / d));



                    //if (alpha < Math.PI && y > -75)
                    //{
                    //    indices.Add(index); // current point
                    //    indices.Add(index + parts + 1);
                    //}

                    if (y < r && alpha > 0)
                    {
                        indices.Add(index);
                        indices.Add(index + 1);
                        indices.Add(index + parts + 1);

                        indices.Add(index + 1);
                        indices.Add(index + 1 + parts + 1);
                        indices.Add(index + 1 + parts);
                    }


                    index += 1;
                }
            }

        }

        static void GenerateMyCoordsSphere(Point3DCollection points, PointCollection textureCoords, Int32Collection indices)
        {
            var parts = 10;
            var delta = Math.PI / parts;
            var r = 75;

            var index = 0;

            for (var y = -75; y <= 75; y += 5)
            {
                var b = (y - 75) / 150.0 * Math.PI;

                var currentRadius = 75 * Math.Abs(Math.Sin(b));

                for (var alpha = Math.PI; alpha >= 0; alpha -= delta)
                {
                    var x = currentRadius * Math.Cos(alpha);
                    var z = currentRadius * Math.Sin(alpha);

                    points.Add(new Point3D(x, y, z));

                    textureCoords.Add(new Point((x + 75.0) / 150, (75.0 - y) / 150));



                    //if (alpha < Math.PI && y > -75)
                    //{
                    //    indices.Add(index); // current point
                    //    indices.Add(index + parts + 1);
                    //}

                    if (y < 75 && alpha > 0)
                    {
                        indices.Add(index);
                        indices.Add(index + 1);
                        indices.Add(index + parts + 1);

                        indices.Add(index + 1);
                        indices.Add(index + 1 + parts + 1);
                        indices.Add(index + 1 + parts);
                    }


                    index += 1;
                }
            }

        }


        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            //var planet = new BigPlanet();
            // var points = planet.Points;
            // var triangles = planet.tri....

            var points = new Point3DCollection();
            var triangles = new Int32Collection();
            var textures = new PointCollection();

            GenerateMyCoords(points, textures, triangles);


            Console.Write("Positions=\"");


            foreach (var point in points)
            {
                Console.Write(Math.Round(point.X, 2) + " " + Math.Round(point.Y, 2) + " " + Math.Round(point.Z, 2));
                Console.Write("  ");
            }

            Console.WriteLine("\"");
            Console.Write("TextureCoordinates=\"");

            foreach (var point in textures)
            {
                Console.Write(point.X + " " + point.Y + "  ");
            }

            Console.WriteLine("\"");
            Console.Write("TriangleIndices=\"");

            for (var i = 0; i < triangles.Count; i += 3)
            {
                Console.Write(triangles[i] + " ");
                Console.Write(triangles[i + 1] + " ");
                Console.Write(triangles[i + 2] + "  ");

            }

            Console.WriteLine("\"");
            Console.WriteLine();

            Console.Read();
        }
    }
}
