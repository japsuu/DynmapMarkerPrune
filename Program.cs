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
        
        private const MarkerPruneMode MODE = MarkerPruneMode.REMOVE_SELECTION_INCLUSIVE;
        
        private const string MARKERS_YAML_FILE_PATH = "markers.yml";
        private const string MARKERS_YAML_OUTPUT_FILE_PATH = "markers_out.yml";
        private const string REGIONS_FILE_PATH = "regions.csv";

        private static IOrderedDictionary<YamlNode, YamlNode> circles;
        private static IOrderedDictionary<YamlNode, YamlNode> markers;

        private static int processedChunks;
        private static int processedEntryCount;

        private static readonly YamlScalarNode WorldNodeReference = new YamlScalarNode("world");
        private static readonly HashSet<string> MarkersToDelete = new HashSet<string>();


        private static void Main()
        {
            LoadMarkers();

            Console.WriteLine("Markers loaded...");

            // Load regions.csv
            string[] selectedRegionsAndChunks = File.ReadAllLines(REGIONS_FILE_PATH);

            int toProcessCount = selectedRegionsAndChunks.Length;

            // Parse and process selected regions and chunks
            foreach (string line in selectedRegionsAndChunks)
            {
                string[] parts = line.Split(';');
                if (parts.Length == 2)
                {
                    // This is a region
                    int regionX = int.Parse(parts[0]);
                    int regionZ = int.Parse(parts[1]);
                    ProcessMarkersInRegion(regionX, regionZ);
                }
                else if (parts.Length == 4)
                {
                    // This is a chunk
                    // int regionX = int.Parse(parts[0]);
                    // int regionZ = int.Parse(parts[1]);
                    int chunkX = int.Parse(parts[2]);
                    int chunkZ = int.Parse(parts[3]);
                    ProcessMarkersInChunk(chunkX, chunkZ);
                }

                processedEntryCount++;
                
                if(processedChunks % 1000 == 0)
                    Console.WriteLine($"Processed {processedChunks} chunks ( {processedEntryCount / (double)toProcessCount * 100d:F1}% )...");
            }
            
            // Open a StreamWriter to the file
            using (StreamWriter writer = File.CreateText("output.txt"))
            {
                // Iterate through the HashSet and write each element to the file
                foreach (string item in MarkersToDelete)
                {
                    writer.WriteLine(item);
                }
            }

            Console.WriteLine($"Found {MarkersToDelete.Count} markers to delete from {processedChunks} deleted chunks.");
                
            SaveModifiedYamlWithDeletedMarkers(MARKERS_YAML_OUTPUT_FILE_PATH);
            
            Console.WriteLine("Done!");
            Console.ReadKey();
        }


        private static void LoadMarkers()
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
        }


        private static void ProcessMarkersInRegion(int regionX, int regionZ)
        {
            int startX = regionX * 512;
            int startZ = regionZ * 512;
            int endX = startX + 512;
            int endZ = startZ + 512;

            ProcessMarkersInRect(startX, startZ, endX, endZ);
            processedChunks += 1024;
        }


        private static void ProcessMarkersInChunk(int chunkX, int chunkZ)   //TODO: Process all markers rather than areas. Loop markers.
        {
            int startX = chunkX * 16;
            int startZ = chunkZ * 16;
            int endX = startX + 16;
            int endZ = startZ + 16;

            ProcessMarkersInRect(startX, startZ, endX, endZ);
            processedChunks++;
        }


        private static void ProcessMarkersInRect(int rectMinX, int rectMinZ, int rectMaxX, int rectMaxZ)
        {
            foreach (KeyValuePair<YamlNode, YamlNode> circleNode in circles)
                CheckMarkerRectOverlap(rectMinX, rectMinZ, rectMaxX, rectMaxZ, circleNode);

            foreach (KeyValuePair<YamlNode, YamlNode> markerNode in markers)
                CheckMarkerRectOverlap(rectMinX, rectMinZ, rectMaxX, rectMaxZ, markerNode);
        }


        private static void CheckMarkerRectOverlap(int rectMinX, int rectMinZ, int rectMaxX, int rectMaxZ, KeyValuePair<YamlNode, YamlNode> markerNode)
        {
            YamlNode key = markerNode.Key;
            YamlMappingNode properties = (YamlMappingNode)markerNode.Value;

            if (!properties["world"].Equals(WorldNodeReference))
                return;

            (int x, int z) = GetCoordinates(properties);

            switch (MODE)
            {
                case MarkerPruneMode.REMOVE_SELECTION_INCLUSIVE:
                    if (x <= rectMinX || x >= rectMaxX || z <= rectMinZ || z >= rectMaxZ)
                        return;
                    break;
                case MarkerPruneMode.REMOVE_SELECTION_EXCLUSIVE:
                    if (x > rectMinX && x < rectMaxX && z > rectMinZ && z < rectMaxZ)
                        return;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!MarkersToDelete.Add(key.ToString()))
                return;

            Console.WriteLine($"Found marker to delete: {key}   (Label: {properties["label"]} | X: {x}, Z: {z})");
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
                    Console.WriteLine($"Removed marker: {markerKeyToDelete} ({label})");
                }
                else if (((YamlMappingNode)mMarkersRoot["circles"]).Children.ContainsKey(markerKeyToDelete))
                {
                    ((YamlMappingNode)mMarkersRoot["circles"]).Children.Remove(markerKeyToDelete);
                    Console.WriteLine($"Removed marker: {markerKeyToDelete} ({label})");
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