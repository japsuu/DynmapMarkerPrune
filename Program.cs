using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using YamlDotNet.Helpers;
using YamlDotNet.RepresentationModel;

namespace DynmapMarkerPrune
{
    internal class Program
    {
        private enum MarkerPruneMode
        {
            REMOVE_SELECTION_INCLUSIVE,
            REMOVE_SELECTION_EXCLUSIVE
        }
        
        private const MarkerPruneMode PRUNE_MODE = MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE;
        
        private const string MARKERS_YAML_FILE_PATH = "markers.yml";
        private const string MARKERS_YAML_OUTPUT_FILE_PATH = "markers_out.yml";
        private const string REGIONS_FILE_PATH = "regions.csv";

        private static IOrderedDictionary<YamlNode, YamlNode> circles;
        private static IOrderedDictionary<YamlNode, YamlNode> markers;

        private static int processedMarkersCount;

        private static readonly YamlScalarNode WorldNodeReference = new YamlScalarNode("world");
        private static readonly Dictionary<string, string> MarkersToDelete = new Dictionary<string, string>();


        private static void Main()
        {
            if (!File.Exists(MARKERS_YAML_FILE_PATH))
            {
                Console.WriteLine("Missing markers.yml -file.");
                return;
            }
            
            LoadAllMarkers();

            // Load regions.csv
            string[] selectedRegionsAndChunks = File.ReadAllLines(REGIONS_FILE_PATH);

            int markersTotalCount = circles.Count + markers.Count;

            foreach (KeyValuePair<YamlNode,YamlNode> markerNode in circles)
            {
                ProcessMarkerNode(markerNode, selectedRegionsAndChunks);

                processedMarkersCount++;
                
                if(processedMarkersCount % 5 == 0)
                    Console.WriteLine($"Processed {processedMarkersCount} markers ( {processedMarkersCount / (double)markersTotalCount * 100d:F1}% )...");
            }

            foreach (KeyValuePair<YamlNode,YamlNode> markerNode in markers)
            {
                ProcessMarkerNode(markerNode, selectedRegionsAndChunks);

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
                
            SaveModifiedYamlWithDeletedMarkers(MARKERS_YAML_OUTPUT_FILE_PATH);
            
            Console.WriteLine("Done!");
            Console.ReadKey();
        }


        private static void ProcessMarkerNode(KeyValuePair<YamlNode, YamlNode> markerNode, string[] selectedRegionsAndChunks)
        {
            YamlMappingNode properties = (YamlMappingNode)markerNode.Value;

            if (!properties["world"].Equals(WorldNodeReference))
                return;

            (int x, int z) = GetCoordinates(properties);

            foreach (string line in selectedRegionsAndChunks)
            {
                bool isInclusive;

                string[] parts = line.Split(';');
                switch (parts.Length)
                {
                    case 2:
                    {
                        // This is a region
                        int regionX = int.Parse(parts[0]);
                        int regionZ = int.Parse(parts[1]);

                        isInclusive = IsMarkerInRegion(regionX, regionZ, x, z);
                        break;
                    }
                    case 4:
                    {
                        // This is a chunk
                        int chunkX = int.Parse(parts[2]);
                        int chunkZ = int.Parse(parts[3]);

                        isInclusive = IsMarkerInChunk(chunkX, chunkZ, x, z);
                        break;
                    }
                    default:
                        throw new Exception("What?");
                }

                if (isInclusive)
                {
                    if(PRUNE_MODE == MarkerPruneMode.REMOVE_SELECTION_INCLUSIVE)
                        AddMarkerForDeletion(markerNode, properties, x, z);
                    
                    return;
                }
            }
            
            if(PRUNE_MODE == MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE)
                AddMarkerForDeletion(markerNode, properties, x, z);
        }


        private static void AddMarkerForDeletion(KeyValuePair<YamlNode, YamlNode> markerNode, YamlMappingNode properties, int x, int z)
        {
            YamlNode key = markerNode.Key;
            string label = properties["label"].ToString();

            MarkersToDelete.Add(key.ToString(), label);

            Console.WriteLine($"Found marker to delete: {key}   (Label: {label} | X: {x}, Z: {z})");
        }


        private static void LoadAllMarkers()
        {
            // Read the YAML file
            string markersYamlContent = File.ReadAllText(MARKERS_YAML_FILE_PATH);

            // Parse the YAML content
            StringReader input = new StringReader(markersYamlContent);
            YamlStream yamlStream = new YamlStream();
            yamlStream.Load(input);

            // Get the root mapping node.
            YamlMappingNode rootNode = (YamlMappingNode)yamlStream.Documents[0].RootNode;

            YamlMappingNode sets = (YamlMappingNode)rootNode["sets"];
            IOrderedDictionary<YamlNode, YamlNode> markersRoot = ((YamlMappingNode)sets["markers"]).Children;
            markers = ((YamlMappingNode)markersRoot["markers"]).Children;
            circles = ((YamlMappingNode)markersRoot["circles"]).Children;
            
            Console.WriteLine("markers.yml loaded...");
        }


        private static bool IsMarkerInRegion(int regionX, int regionZ, int markerX, int markerZ)
        {
            int startX = regionX * 512;
            int startZ = regionZ * 512;
            int endX = startX + 512;
            int endZ = startZ + 512;

            return CheckMarkerRectOverlap(startX, startZ, endX, endZ, markerX, markerZ);
        }


        private static bool IsMarkerInChunk(int chunkX, int chunkZ, int markerX, int markerZ)
        {
            int startX = chunkX * 16;
            int startZ = chunkZ * 16;
            int endX = startX + 16;
            int endZ = startZ + 16;

            return CheckMarkerRectOverlap(startX, startZ, endX, endZ, markerX, markerZ);
        }


        private static bool CheckMarkerRectOverlap(int rectMinX, int rectMinZ, int rectMaxX, int rectMaxZ, int markerX, int markerZ)
        {
            return markerX >= rectMinX && markerX < rectMaxX && markerZ >= rectMinZ && markerZ < rectMaxZ;
        }


        private static (int x, int z) GetCoordinates(YamlMappingNode node)
        {
            return ((int)double.Parse(node["x"].ToString(), CultureInfo.InvariantCulture),
                (int)double.Parse(node["z"].ToString(), CultureInfo.InvariantCulture));
        }
        
        
        private static void SaveModifiedYamlWithDeletedMarkers(string outputPath)
        {
            // Read the YAML file
            string markersYamlContent = File.ReadAllText(MARKERS_YAML_FILE_PATH);

            // Parse the YAML content
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
    
            // Remove markers from the new YamlMappingNode
            foreach ((YamlNode markerKeyToDelete, YamlNode label) in nodesToRemove)
            {
                if (((YamlMappingNode)mMarkersRoot["markers"]).Children.ContainsKey(markerKeyToDelete))
                {
                    ((YamlMappingNode)mMarkersRoot["markers"]).Children.Remove(markerKeyToDelete);
                    //Console.WriteLine($"Removed marker: {markerKeyToDelete} ({label})");
                }
                else if (((YamlMappingNode)mMarkersRoot["circles"]).Children.ContainsKey(markerKeyToDelete))
                {
                    ((YamlMappingNode)mMarkersRoot["circles"]).Children.Remove(markerKeyToDelete);
                    //Console.WriteLine($"Removed marker: {markerKeyToDelete} ({label})");
                }
            }
            
            // Create a new YamlStream to hold the modified data
            YamlStream modifiedYamlStream = new YamlStream();
    
            // Clone the original YAML's root node to the modified stream
            modifiedYamlStream.Documents.Add(new YamlDocument(rootNode));
    
            // Save the modified YAML to a new file
            using (StreamWriter writer = File.CreateText(outputPath))
            {
                writer.WriteLine("%YAML 1.1");
                writer.WriteLine("---");
                modifiedYamlStream.Save(writer, assignAnchors: false);
            }
            //TODO: Remove the three dots that are generated at the end of the document.

            Console.WriteLine($"Modified YAML with deleted markers saved to: {outputPath}");
        }
    }
}