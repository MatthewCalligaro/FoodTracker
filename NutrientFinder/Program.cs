using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NutrientFinder
{
    class Program
    {
        /// <summary>
        /// The relative path of the file containing the FDC IDs of the foods to look up.
        /// </summary>
        private const string InputFilename = "../../../../FdcIds.txt";

        /// <summary>
        /// The relative path of the file to write the retrieved nutrition information.
        /// </summary>
        private const string OutputFilename = "../../../../UsdaFdcFoodNutrients.csv";

        /// <summary>
        /// The maximum number of foods which can be requested from the FDC API per call.
        /// </summary>
        private const int MaxFoodsPerRequest = 20;

        /// <summary>
        /// The URI (including key) of the FDC API which returns food nutrition information.
        /// </summary>
        private const string RequestUri
            = "https://api.nal.usda.gov/fdc/v1/foods?api_key=psJzyUfgx5kVvXEAES0aRy5sAqfE8ZKFo0zhDBAw";

        /// <summary>
        /// Information about the nutrients we wish to query for each food.
        /// </summary>
        /// <remarks>
        /// 0th element = nutrient ID
        /// 1st element = desired nutrient unit
        /// 2nd element = nutrient name
        /// </remarks>
        private static readonly (int, Unit, string)[] DesiredNutrients =
        {
            (208, Unit.NotMass, "Calories"),
            (204, Unit.G, "Total fat"),
            (606, Unit.G, "Saturated fat"),
            (646, Unit.G, "Polyunsaturated fat"),
            (645, Unit.G, "Monounsaturated fat"),
            (205, Unit.G, "Total carb"),
            (291, Unit.G, "Dietary fiber"),
            (269, Unit.G, "Sugar"),
            (203, Unit.G, "Total protein"),
            (512, Unit.MG, "Histidine"),
            (503, Unit.MG, "Isoleucine"),
            (504, Unit.MG, "Leucine"),
            (505, Unit.MG, "Lysine"),
            (506, Unit.MG, "Methionine"),
            (508, Unit.MG, "Phenylalanine"),
            (502, Unit.MG, "Threonine"),
            (501, Unit.MG, "Tryptophan"),
            (510, Unit.MG, "Valine"),
            (601, Unit.MG, "Cholesterol"),
            (320, Unit.UG, "Vitamin A"),
            (404, Unit.MG, "Vitamin B1 Thiamin"),
            (405, Unit.MG, "Vitamin B2 Riboflavin"),
            (406, Unit.MG, "Vitamin B3 Niacin"),
            (410, Unit.MG, "Vitamin B5 Pantothenic acid"),
            (415, Unit.MG, "Vitamin B6"),
            (-1, Unit.UG, "Vitamin B7 Biotin"),
            (417, Unit.UG, "Vitamin B9 Folate"),
            (418, Unit.UG, "Vitamin B12"),
            (401, Unit.MG, "Vitamin C"),
            (328, Unit.UG, "Vitamin D"),
            (323, Unit.MG, "Vitamin E"),
            (430, Unit.UG, "Vitamin K"),
            (421, Unit.MG, "Choline"),
            (307, Unit.MG, "Na"),
            (304, Unit.MG, "Mg"),
            (305, Unit.MG, "P"),
            (306, Unit.MG, "K"),
            (301, Unit.MG, "Ca"),
            (-2, Unit.UG, "Cr"),
            (315, Unit.MG, "Mn"),
            (303, Unit.MG, "Fe"),
            (312, Unit.UG, "Cu"),
            (309, Unit.MG, "Zn"),
            (317, Unit.UG, "Se"),
            (-3, Unit.UG, "Mo"),
            (-4, Unit.UG, "I"),
        };

        /// <summary>
        /// Program entry point.
        /// </summary>
        public static void Main()
        {
            int[] fdcIds = ReadInputFile(InputFilename);
            CreateOutputFile(fdcIds, OutputFilename);
        }

        /// <summary>
        /// Read the FDC IDs of the foods to look up.
        /// </summary>
        /// <param name="filename">The relative path of a text file containing the FDC IDs.</param>
        /// <returns>The FDC IDs of the foods to look up.</returns>
        private static int[] ReadInputFile(string filename)
        {
            using StreamReader file = new(filename);

            // The input file should be a list of FDC IDs, all of which should parse to int
            return file.ReadToEnd().Split('\n').Select(int.Parse).ToArray();
        }

        /// <summary>
        /// Look up the desired nutrients for each food an save this information to a CSV.
        /// </summary>
        /// <param name="foodIds">The FDC IDs of the foods to look up.</param>
        /// <param name="filename">The relative path of the CSV to write the nutrient information.</param>
        private static void CreateOutputFile(int[] foodIds, string filename)
        {
            // Request food info in MaxFoodsPerRequest sized chunks
            List<Food> foods = new();
            for (int i = 0; i < foodIds.Length; i += MaxFoodsPerRequest)
            {
                int[] currentChunk = new int[Math.Min(MaxFoodsPerRequest, foodIds.Length - i)];
                Array.Copy(foodIds, i, currentChunk, 0, currentChunk.Length);
                foods.AddRange(RequestFoodInfo(currentChunk).GetAwaiter().GetResult());
            }

            using StreamWriter file = new(filename);

            file.WriteLine(CreateOutputHeader());
            foreach (Food food in foods)
            {
                file.WriteLine(ExtractFoodInfo(food));
            }
        }

        /// <summary>
        /// Returns the header of the output CSV.
        /// </summary>
        /// <returns>A string representing one CSV line containing the header for each column.</returns>
        private static string CreateOutputHeader()
        {
            StringBuilder header = new("Food,FDC ID");

            foreach ((_, Unit unit, string name) in DesiredNutrients)
            {
                header.Append($",{name} ({unit.ToString()})");
            }

            return header.ToString();
        }

        /// <summary>
        /// Format the nutrient information of a food as a CSV line.
        /// </summary>
        /// <param name="food">The nutrient information returned by the FDC API for the food.</param>
        /// <returns>A string representing one CSV line containing the nutrient information for that food.</returns>
        private static string ExtractFoodInfo(Food food)
        {
            // Replace any commas in the food title with semicolons to preserve the csv
            StringBuilder line = new($"{food.description.Replace(",", ";")},{food.fdcId}");

            // Store food.foodNutrients in a dictionary mapping nutrientId to (amount, unit)
            Dictionary<int, (double, Unit)> nutrientDict =
                new(food.foodNutrients.Select(nutrient => new KeyValuePair<int, (double, Unit)>(
                    int.Parse(nutrient.number),
                    (nutrient.amount, Enum.TryParse(nutrient.unitName, out Unit unit) ? unit : Unit.NotMass))));

            // Lookup the amount of each desired nutrient in our dictionary
            foreach ((int nutrientId, Unit desiredUnit, _) in DesiredNutrients)
            {
                double amount = 0;
                if (nutrientDict.ContainsKey(nutrientId))
                {
                    (amount, Unit actualUnit) = nutrientDict[nutrientId];

                    // Convert units if necessary
                    if (actualUnit != desiredUnit)
                    {
                        amount *= (int)desiredUnit;
                        amount /= (int)actualUnit;
                    }
                }

                // Round to at most the thousandths place
                line.Append($",{amount:0.###}");
            }

            return line.ToString();
        }

        /// <summary>
        /// Requests the nutrition information for a list of foods from the FDC API.
        /// </summary>
        /// <param name="foodIds">The FDC IDs of the foods to look up.</param>
        /// <returns>Food objects containing the nutrient information for each food, sorted by FDC ID.</returns>
        /// <exception cref="InvalidOperationException">Thrown if foodIds is too large for the API.</exception>
        /// <remarks>See API documentation at https://fdc.nal.usda.gov/</remarks>
        private static async Task<Food[]> RequestFoodInfo(int[] foodIds)
        {
            if (foodIds.Length > MaxFoodsPerRequest)
            {
                throw new InvalidOperationException(
                    $"Cannot retrieve more than [{MaxFoodsPerRequest}] foods per request");
            }

            RequestBody requestBody = new(foodIds, "abridged");
            JsonContent content = JsonContent.Create(requestBody);

            using HttpClient client = new();
            HttpResponseMessage response = await client.PostAsync(RequestUri, content);
            Food[] foods = JsonSerializer.Deserialize<Food[]>(response.Content.ReadAsStream());

            // The response does not necessarily return the foods in the order we requested, so sort before returning
            Array.Sort(foods, new FoodComparer());
            return foods;
        }
    }

    /// <summary>
    /// Represents mass units with values normalized to the gram.
    /// </summary>
    public enum Unit
    {
        NotMass = 0,
        G = 1,
        MG = 1000,
        UG = 1000000,
    }

    /// <summary>
    /// The JSON object sent in the body of the POST request to the FDC API.
    /// </summary>
    /// <param name="fdcIds">An array of FDC IDs representing the foods to look up.</param>
    /// <param name="format">"full" or "abridged", indicating the amount of nutrition information to return.</param>
    public record RequestBody(int[] fdcIds, string format);

    /// <summary>
    /// The JSON object returned by the FDC API to represent one nutrient of one food.
    /// </summary>
    /// <param name="number">The integer ID of the nutrient (stored as a string).</param>
    /// <param name="name">The name of the nutrient.</param>
    /// <param name="amount">The amount of the nutrient in the food.</param>
    /// <param name="unitName">The unit of the nutrient.</param>
    public record Nutrient(
        string number,
        string name,
        double amount,
        string unitName);

    /// <summary>
    /// The JSON object returned by the FDC API to represent one food.
    /// </summary>
    /// <param name="fdcId">The integer ID uniquely identifying the food.</param>
    /// <param name="description">The name of the food.</param>
    /// <param name="dataType">The USDA dataset in which the food is stored.</param>
    /// <param name="publicationDate">The date at which the USDA published this food's nutrition data.</param>
    /// <param name="nbdNumber">A second integer ID used to represent the food.</param>
    /// <param name="foodNutrients">Information about each nutrient in the food.</param>
    public record Food(
        int fdcId,
        string description,
        string dataType,
        DateTime publicationDate,
        int nbdNumber,
        Nutrient[] foodNutrients);

    /// <summary>
    /// Compares two foods based on their FDC ID.
    /// </summary>
    public class FoodComparer : IComparer<Food>
    {
        int IComparer<Food>.Compare(Food x, Food y)
        {
            return x.fdcId - y.fdcId;
        }
    }
}
