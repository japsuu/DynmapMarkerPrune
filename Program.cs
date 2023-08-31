using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using YamlDotNet.Helpers;
using YamlDotNet.RepresentationModel;

namespace DynmapMarkerPrune
{
    internal class Program
    {
        // Config.
        private static MarkerPruneMode pruneMode = MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE;
        private static string markersYamlFilePath = "markers.yml";
        private static string markersYamlOutputFilePath = "markers_pruned.yml";
        private static string selectionsCsvFilePath = "selections.csv";
        
        // Runtime.
        private static int processedMarkersCount;
        private static IOrderedDictionary<YamlNode, YamlNode> circles;
        private static IOrderedDictionary<YamlNode, YamlNode> markers;
        private static readonly List<SelectionArea> Selections = new List<SelectionArea>();
        private static readonly YamlScalarNode WorldNodeReference = new YamlScalarNode("world");
        private static readonly Dictionary<string, string> MarkersToDelete = new Dictionary<string, string>();


        private static void Main(string[] args)
        {
            if(!TryParseArguments(args))
                return;
            
            if (!File.Exists(markersYamlFilePath))
            {
                Console.WriteLine("Missing markers.yml -file.");
                return;
            }
            
            if (!File.Exists(selectionsCsvFilePath))
            {
                Console.WriteLine("Missing selections.csv -file.");
                return;
            }
            
            LoadMarkers();

            LoadSelectionAreas();

            int markersTotalCount = circles.Count + markers.Count;
            
            Console.WriteLine($"Start pruning {markersTotalCount} markers with prune mode {pruneMode}...");
            Thread.Sleep(1000);

            foreach (KeyValuePair<YamlNode,YamlNode> markerNode in circles)
            {
                ProcessMarkerNode(markerNode);

                processedMarkersCount++;
                
                if(processedMarkersCount % 5 == 0)
                    Console.WriteLine($"Processed {processedMarkersCount} markers ( {processedMarkersCount / (double)markersTotalCount * 100d:F1}% )...");
            }

            foreach (KeyValuePair<YamlNode,YamlNode> markerNode in markers)
            {
                ProcessMarkerNode(markerNode);

                processedMarkersCount++;
                
                if(processedMarkersCount % 5 == 0)
                    Console.WriteLine($"Processed {processedMarkersCount} markers ( {processedMarkersCount / (double)markersTotalCount * 100d:F1}% )...");
            }
            
            // Open a StreamWriter to the file
            using (StreamWriter writer = File.CreateText("removed_marker_ids.txt"))
            {
                // Iterate through the HashSet and write each element to the file
                foreach (KeyValuePair<string, string> pair in MarkersToDelete)
                {
                    writer.WriteLine($"{pair.Value} \t (id {pair.Key})");
                }
            }

            Console.WriteLine($"Found {MarkersToDelete.Count} markers to delete out of {markersTotalCount} markers in total.");
                
            SaveModifiedYaml(markersYamlOutputFilePath);
            
            Console.WriteLine("Done!");
            Console.ReadKey();
        }


        private static bool TryParseArguments(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                Console.WriteLine("No arguments provided, using default values...");
                return true;
            }
            
            if (args.Count != 4)
            {
                Console.WriteLine("Invalid argument count provided, using default values...");
                Console.WriteLine("Argument order -> markers_path selections_path output_path prune_mode (inclusive or exclusive)");
                return false;
            }

            markersYamlFilePath = args[0];
            selectionsCsvFilePath = args[1];
            markersYamlOutputFilePath = args[2];
            pruneMode = args[3] == "inclusive" ? MarkerPruneMode.REMOVE_SELECTION_INCLUSIVE : MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE;
            
            return true;
        }


        private static void LoadMarkers()
        {
            // Read the YAML file.
            string markersYamlContent = File.ReadAllText(markersYamlFilePath);

            // Parse the YAML content.
            StringReader input = new StringReader(markersYamlContent);
            YamlStream yamlStream = new YamlStream();
            yamlStream.Load(input);

            // Get the root mapping node.
            YamlMappingNode rootNode = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            YamlMappingNode sets = (YamlMappingNode)rootNode["sets"];
            IOrderedDictionary<YamlNode, YamlNode> markersRoot = ((YamlMappingNode)sets["markers"]).Children;
            markers = ((YamlMappingNode)markersRoot["markers"]).Children;
            circles = ((YamlMappingNode)markersRoot["circles"]).Children;
            
            Console.WriteLine($"{markersYamlFilePath} loaded...");
        }


        private static void LoadSelectionAreas()
        {
            string[] selectedRegionsAndChunks = File.ReadAllLines(selectionsCsvFilePath);
            
            foreach (string line in selectedRegionsAndChunks)
            {
                string[] parts = line.Split(';');
                switch (parts.Length)
                {
                    case 2:
                    {
                        // This is a region.
                        int regionX = int.Parse(parts[0]);
                        int regionZ = int.Parse(parts[1]);
                        
                        Selections.Add(new SelectionArea(regionX, regionZ, 512));

                        break;
                    }
                    case 4:
                    {
                        // This is a chunk.
                        int chunkX = int.Parse(parts[2]);
                        int chunkZ = int.Parse(parts[3]);
                        
                        Selections.Add(new SelectionArea(chunkX, chunkZ, 16));

                        break;
                    }
                    default:
                        throw new Exception("Unexpected selection file format, expected 2 or 4 coordinates per line.");
                }
            }
            
            Console.WriteLine($"{selectionsCsvFilePath} loaded...");
        }


        private static void ProcessMarkerNode(KeyValuePair<YamlNode, YamlNode> markerNode)
        {
            YamlMappingNode properties = (YamlMappingNode)markerNode.Value;

            if (!properties["world"].Equals(WorldNodeReference))
                return;

            (int x, int z) = GetCoordinatesFromYamlNode(properties);

            foreach (SelectionArea selection in Selections)
            {
                if (!selection.ContainsPosition(x, z))
                    continue;
                
                if(pruneMode == MarkerPruneMode.REMOVE_SELECTION_INCLUSIVE)
                    AddMarkerForDeletion(markerNode, properties, x, z);
                    
                return;
            }
            
            if(pruneMode == MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE)
                AddMarkerForDeletion(markerNode, properties, x, z);
        }


        private static void AddMarkerForDeletion(KeyValuePair<YamlNode, YamlNode> markerNode, YamlMappingNode properties, int x, int z)
        {
            YamlNode key = markerNode.Key;
            string label = properties["label"].ToString();

            MarkersToDelete.Add(key.ToString(), label);

            Console.WriteLine($"Found marker to delete: {key}   (Label: {label} | X: {x}, Z: {z})");
        }


        private static (int x, int z) GetCoordinatesFromYamlNode(YamlMappingNode node)
        {
            return ((int)double.Parse(node["x"].ToString(), CultureInfo.InvariantCulture),
                (int)double.Parse(node["z"].ToString(), CultureInfo.InvariantCulture));
        }
        
        
        private static void SaveModifiedYaml(string outputPath)
        {
            string markersYamlContent = File.ReadAllText(markersYamlFilePath);

            // Parse the YAML content.
            StringReader input = new StringReader(markersYamlContent);
            YamlStream yamlStream = new YamlStream();
            yamlStream.Load(input);

            // Get the root mapping node.
            YamlMappingNode rootNode = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            YamlMappingNode sets = (YamlMappingNode)rootNode["sets"];
            IOrderedDictionary<YamlNode, YamlNode> markersRoot = ((YamlMappingNode)sets["markers"]).Children;
            markers = ((YamlMappingNode)markersRoot["markers"]).Children;
            circles = ((YamlMappingNode)markersRoot["circles"]).Children;

            List<(YamlNode node, YamlNode label)> nodesToRemove = new List<(YamlNode node, YamlNode label)>();

            foreach (KeyValuePair<YamlNode, YamlNode> node in markers)
            {
                YamlNode key = node.Key;

                if (!MarkersToDelete.Remove(key.ToString()))
                    continue;
                
                YamlMappingNode properties = (YamlMappingNode)node.Value;
                nodesToRemove.Add((key, properties["label"]));
            }

            YamlMappingNode mSets = (YamlMappingNode)rootNode["sets"];
            IOrderedDictionary<YamlNode, YamlNode> mMarkersRoot = ((YamlMappingNode)mSets["markers"]).Children;
    
            // Remove markers from the new YamlMappingNode.
            foreach ((YamlNode markerKeyToDelete, YamlNode _) in nodesToRemove)
            {
                YamlMappingNode markersRootNode = (YamlMappingNode)mMarkersRoot["markers"];
                if (markersRootNode.Children.ContainsKey(markerKeyToDelete))
                {
                    markersRootNode.Children.Remove(markerKeyToDelete);
                }
                else
                {
                    YamlMappingNode circlesRootNode = (YamlMappingNode)mMarkersRoot["circles"];
                    if (circlesRootNode.Children.ContainsKey(markerKeyToDelete))
                    {
                        circlesRootNode.Children.Remove(markerKeyToDelete);
                    }
                }
            }
            
            // Create a new YamlStream to hold the modified data.
            YamlStream modifiedYamlStream = new YamlStream();
    
            // Clone the original YAML's root node to the modified stream.
            modifiedYamlStream.Documents.Add(new YamlDocument(rootNode));
    
            // Save the modified YAML to a new file.
            using (StreamWriter writer = File.CreateText(outputPath))
            {
                // Add the YAML prefix.
                writer.WriteLine("%YAML 1.1");
                writer.WriteLine("---");
                modifiedYamlStream.Save(writer, assignAnchors: false);
            }

            Console.WriteLine($"Modified YAML saved to: {outputPath}");
        }
    }
}