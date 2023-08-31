namespace DynmapMarkerPrune
{
    public class SelectionArea
    {
        private readonly int _startX;
        private readonly int _startZ;
        private readonly int _endX;
        private readonly int _endZ;

        
        public bool ContainsPosition(int x, int z) => x >= _startX && x < _endX && z >= _startZ && z < _endZ;
        
        
        public SelectionArea(int startX, int startZ, int areaSize)
        {
            _startX = startX * areaSize;
            _startZ = startZ * areaSize;
            _endX = _startX + areaSize;
            _endZ = _startZ + areaSize;
        }
    }
}