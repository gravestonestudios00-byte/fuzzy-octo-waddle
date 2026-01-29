using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MineGenV3_Improved : MonoBehaviour
{
    [Header("Grid")]
    public int width = 90;
    public int height = 70;
    public int margin = 5;
    public float cellSize = 5f;
    public int seed = 0;

    [Header("Hallway Growth")]
    public int totalSteps = 450;
    [Range(0f, 1f)] public float turnChance = 0.25f;
    [Range(0f, 1f)] public float branchChance = 0.15f;
    [Range(1, 10)] public int minStraightBeforeTurn = 3;

    [Header("Explore Drive")]
    [Range(0f, 1f)] public float exploreDrive = 0.85f;

    [Header("Rooms")]
    [Range(0f, 1f)] public float roomChanceAtEndpoint = 0.45f;
    public int targetRoomCount = 8;
    public Vector2Int roomSize = new Vector2Int(4, 8);
    public int roomBuffer = 3;
    [Range(1, 4)] public int minRoomDoors = 1;
    [Range(1, 5)] public int maxRoomDoors = 2;
    
    [Header("Storage Rooms")]
    [Range(0, 20)] public int storageRoomCount = 5;
    [Tooltip("If true, only create storage rooms connected to hallways (auto-door). If false, can also connect to rooms (room system handles doors).")]
    public bool storageRoomsOnlyInHallways = true;

    [Header("Build Settings")]
    public bool buildGeometry = true;
    public float floorThickness = 0.2f;
    public float wallHeight = 4.0f;
    public float wallThickness = 0.2f;
    public Transform generatedRoot;
    
    [Header("Prefab Models (Optional)")]
    [Tooltip("Leave empty to use placeholder cubes")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject roofPrefab;
    public GameObject lightRoofPrefab;
    public GameObject lightPrefab;
    public GameObject brokenLightPrefab;
    public GameObject doorFramePrefab;
    public GameObject doorPrefab;
    public GameObject dropBoxPrefab;
    public GameObject mainDropBoxPrefab;
    public GameObject mainDoorPrefab;
    public GameObject mainDoorFramePrefab;
    
    [Header("Player Start Wall Prefabs (Optional)")]
    [Tooltip("Custom prefabs for player start area walls")]
    public GameObject playerStartWallPrefab;
    public GameObject playerStartUpperWallPrefab;
    public GameObject playerStartFillWallPrefab;
    public GameObject playerStartDoorPrefab;
    public GameObject playerStartDoorFramePrefab;
    
    [Header("Custom Wall Settings")]
    [Tooltip("Custom size ONLY for Wall_PlayerStart (width, height, depth). Walls next to/above doors keep original size.")]
    public Vector3 customWallSize = new Vector3(10f, 10f, 8f);
    [Tooltip("Enable to use custom wall size for PlayerStart room walls. Disable to use default calculated size.")]
    public bool useCustomWallSize = false;
    [Tooltip("Height of UpperWall_PlayerStart above doors")]
    public float upperWallHeight = 6f;
    [Tooltip("Height of FillWall_PlayerStart (fills gaps above doors)")]
    public float fillWallHeight = 10f;
    [Tooltip("Size of FillWall_PlayerStart (width, height, depth). X should always be 9, Y should be 8.")]
    public Vector3 fillWallSize = new Vector3(9f, 8f, 0.2f);
    [Tooltip("Y offset to move Wall_PlayerStart down (negative = down)")]
    public float wallYOffset = 0f;
    [Tooltip("Y offset to move UpperWall_PlayerStart down (negative = down)")]
    public float upperWallYOffset = 0f;
    [Tooltip("Y offset to move FillWall_PlayerStart down (negative = down)")]
    public float fillWallYOffset = 0f;
    [Tooltip("Left/Right offset for UpperWall_PlayerStart (adjusts X for North/South, Z for East/West)")]
    public float upperWallLateralOffset = 0f;
    [Tooltip("Left/Right offset for FillWall_PlayerStart (adjusts X for North/South, Z for East/West)")]
    public float fillWallLateralOffset = 0f;
    
    [Header("Roof Settings")]
    public bool generateRoofs = true;
    public float roofThickness = 0.2f;
    
    [Header("Special Rooms")]
    public float playerStartRoomHeight = 20.0f;
    
    [Header("Hard Block Settings")]
    [Tooltip("Minimum distance from start areas where storage rooms and lights cannot spawn")]
    public int hardBlockRadius = 10;

    private System.Random rng;
    private enum Cell { Empty, Hall, Room, PlayerStart, Exit, GenStart, Wall, StorageRoom }
    private Cell[,] grid;
    private List<Endpoint> frontier = new List<Endpoint>();
    private List<Vector2Int> endpoints = new List<Vector2Int>();
    private int roomCount = 0;
    private Vector2Int genStartCell;
    private Vector2Int playerStartCell;
    private Vector2Int endCell;
    private RectInt playerStartRect;
    private List<RectInt> roomRects = new List<RectInt>();
    private Dictionary<RectInt, string> roomCodes = new Dictionary<RectInt, string>();
    private List<Vector2Int> storageRooms = new List<Vector2Int>();
    private Dictionary<Vector2Int, string> storageRoomCodes = new Dictionary<Vector2Int, string>();
    private Dictionary<Vector2Int, bool> storageRoomConnectsToHallway = new Dictionary<Vector2Int, bool>();
    private Dictionary<Vector2Int, Vector2Int> storageRoomDoorwayPositions = new Dictionary<Vector2Int, Vector2Int>();
    private Dictionary<Vector2Int, HashSet<Vector2Int>> roomDoorways = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private static readonly Vector2Int[] Dirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private struct Endpoint
    {
        public Vector2Int cell;
        public Vector2Int dir;
        public int straight;
        public Endpoint(Vector2Int c, Vector2Int d) { cell = c; dir = d; straight = 0; }
    }

    private bool generationFailed = false;
    private const int MAX_GENERATION_RETRIES = 10;
    
    public bool IsGenerationComplete { get; private set; } = false;
    public System.Action OnGenerationComplete;
    
    public Vector3 GetPlayerStartWorldPosition()
    {
        if (playerStartRect.width > 0)
        {
            int centerX1 = playerStartRect.x + (playerStartRect.width / 2) - 1;
            int centerX2 = playerStartRect.x + (playerStartRect.width / 2);
            int centerY1 = playerStartRect.y + (playerStartRect.height / 2) - 1;
            int centerY2 = playerStartRect.y + (playerStartRect.height / 2);
            
            Vector3 pos1 = CellToWorld(new Vector2Int(centerX1, centerY1));
            Vector3 pos2 = CellToWorld(new Vector2Int(centerX2, centerY1));
            Vector3 pos3 = CellToWorld(new Vector2Int(centerX1, centerY2));
            Vector3 pos4 = CellToWorld(new Vector2Int(centerX2, centerY2));
            
            Vector3 centerPosition = (pos1 + pos2 + pos3 + pos4) / 4f;
            centerPosition.y = 0;
            
            return centerPosition;
        }
        return Vector3.zero;
    }
    
    public Vector3 GetPlayerStartDoorDirection()
    {
        if (playerStartRect.width > 0)
        {
            Vector2Int centerCell = new Vector2Int(
                playerStartRect.x + playerStartRect.width / 2,
                playerStartRect.y + playerStartRect.height / 2
            );
            
            for (int d = 0; d < 4; d++)
            {
                Vector2Int checkDir = Dirs[d];
                
                for (int dist = 1; dist <= playerStartRect.width / 2 + 2; dist++)
                {
                    Vector2Int checkCell = centerCell + checkDir * dist;
                    
                    if (InBounds(checkCell))
                    {
                        if (grid[checkCell.x, checkCell.y] == Cell.Hall || grid[checkCell.x, checkCell.y] == Cell.Room)
                        {
                            return new Vector3(checkDir.x, 0, checkDir.y);
                        }
                    }
                }
            }
        }
        
        return Vector3.forward;
    }
    
    public float GetCellSize()
    {
        return cellSize;
    }
    
    private void Start()
    {
        Generate();
    }
    
    [ContextMenu("Generate")]
    public void Generate()
    {
        for (int attempt = 1; attempt <= MAX_GENERATION_RETRIES; attempt++)
        {
            generationFailed = false;
            
            if (attempt > 1)
            {
                Debug.LogWarning($"Regenerating maze (Attempt {attempt}/{MAX_GENERATION_RETRIES})...");
            }
            
            GenerateInternal();
            
            if (!generationFailed)
            {
                if (attempt > 1)
                {
                    Debug.Log($"✓ Successfully generated maze on attempt {attempt}");
                }
                IsGenerationComplete = true;
                OnGenerationComplete?.Invoke();
                return;
            }
            
            ClearGenerated();
        }
        
        Debug.LogError($"Failed to generate valid maze after {MAX_GENERATION_RETRIES} attempts!");
    }
    
    private void GenerateInternal()
    {
        rng = (seed == 0) ? new System.Random(Guid.NewGuid().GetHashCode()) : new System.Random(seed);
        if (generatedRoot == null) generatedRoot = transform;
        ClearGenerated();

        grid = new Cell[width, height];
        frontier.Clear();
        endpoints.Clear();
        roomRects.Clear();
        roomCodes.Clear();
        storageRooms.Clear();
        storageRoomCodes.Clear();
        storageRoomConnectsToHallway.Clear();
        storageRoomDoorwayPositions.Clear();
        roomCount = 0;
        
        floorCounter = 0;
        wallCounter = 0;
        upperWallCounter = 0;
        roofCounter = 0;

        genStartCell = new Vector2Int(width / 2, height / 2);
        SetCell(genStartCell, Cell.GenStart);

        foreach (var d in Dirs) frontier.Add(new Endpoint(genStartCell, d));

        for (int step = 0; step < totalSteps; step++)
        {
            if (frontier.Count == 0) break;
            int idx = PickEndpointIndex();
            Endpoint ep = frontier[idx];

            if (TryExtendEndpoint(ref ep))
            {
                frontier[idx] = ep;
                if (rng.NextDouble() < branchChance && ep.straight >= minStraightBeforeTurn)
                {
                    Vector2Int bdir = (rng.NextDouble() < 0.5) ? TurnLeft(ep.dir) : TurnRight(ep.dir);
                    frontier.Add(new Endpoint(ep.cell, bdir));
                }
            }
            else
            {
                endpoints.Add(ep.cell);
                if (rng.NextDouble() < roomChanceAtEndpoint) 
                {
                    if (TryMakeRoomAtEndpoint(ep.cell, ep.dir, roomBuffer))
                        roomCount++;
                }
                frontier.RemoveAt(idx);
            }
        }

        if (roomCount < targetRoomCount)
        {
            AddRoomsToReachTarget();
        }

        PlacePlayerStart();
        
        if (generationFailed)
        {
            return;
        }
        
        if (playerStartRect.width == 0)
        {
            Debug.LogError("Player start was not successfully placed!");
            generationFailed = true;
            return;
        }
        
        CreateStorageRooms();
        Convert1x1RoomsToStorageRooms();
        RemoveStorageRoomsConnectedToStart();
        PlaceEndRoof();

        if (buildGeometry)
        {
            BuildGeometry();
            SeparateTouchingRooms();
            EnforceDoorLimitsPostGeneration();
            PlaceDoors();
            RemoveWallsOverlappingPlayerStartDoor();
            FixStorageRoomDoorScales();
            PlaceRoomLights();
            FillDarkAreasWithBrokenLights();
            PlaceDropBoxes();
            PlacePlayerStartPlatforms();
            
            FixAllMaterialsAfterGeneration();
        }
    }
    
    private void FixAllMaterialsAfterGeneration()
    {
        RuntimeMaterialFixer materialFixer = FindAnyObjectByType<RuntimeMaterialFixer>();
        
        if (materialFixer != null)
        {
            materialFixer.FixAllMaterialsNow();
        }
        else
        {
            Debug.LogWarning("[MineGen] RuntimeMaterialFixer not found in scene - materials may be shiny!");
        }
    }

    private void CreateStorageRooms()
    {
        if (storageRoomCount <= 0) return;
        
        Vector2Int startCell = new Vector2Int((int)playerStartRect.x, (int)playerStartRect.y);
        
        List<(Vector2Int connectionCell, bool isHallway)> candidates = new List<(Vector2Int, bool)>();
        
        for (int x = margin; x < width - margin; x++)
        {
            for (int y = margin; y < height - margin; y++)
            {
                Cell currentCell = grid[x, y];
                
                if (currentCell == Cell.Hall || currentCell == Cell.Room || currentCell == Cell.PlayerStart)
                {
                    Vector2Int currentPos = new Vector2Int(x, y);
                    
                    if (Vector2Int.Distance(currentPos, startCell) < 3f)
                        continue;
                    
                    bool isHallway = currentCell == Cell.Hall;
                    int neighborCount = 0;
                    bool hasEmptyNeighbor = false;
                    
                    foreach (var dir in Dirs)
                    {
                        Vector2Int neighbor = new Vector2Int(x, y) + dir;
                        if (!InBounds(neighbor)) continue;
                        
                        Cell neighborCell = grid[neighbor.x, neighbor.y];
                        if (neighborCell == Cell.Hall || neighborCell == Cell.Room || neighborCell == Cell.PlayerStart)
                            neighborCount++;
                        else if (neighborCell == Cell.Empty)
                            hasEmptyNeighbor = true;
                    }
                    
                    if (neighborCount >= 1 && hasEmptyNeighbor)
                    {
                        candidates.Add((new Vector2Int(x, y), isHallway));
                    }
                }
            }
        }
        
        Shuffle(candidates);
        
        int created = 0;
        foreach (var (connectionCell, isHallway) in candidates)
        {
            if (created >= storageRoomCount) break;
            
            if (storageRoomsOnlyInHallways && !isHallway)
                continue;
            
            foreach (var dir in Dirs)
            {
                Vector2Int storageCell = connectionCell + dir;
                if (!InBounds(storageCell)) continue;
                if (grid[storageCell.x, storageCell.y] != Cell.Empty) continue;
                
                if (IsInHardBlockZone(storageCell))
                    continue;
                
                if (Vector2Int.Distance(storageCell, startCell) < 3f)
                    continue;
                
                bool validSpot = true;
                foreach (var checkDir in Dirs)
                {
                    Vector2Int adjacentCell = storageCell + checkDir;
                    if (!InBounds(adjacentCell)) continue;
                    if (adjacentCell == connectionCell) continue;
                    
                    Cell adjacentType = grid[adjacentCell.x, adjacentCell.y];
                    if (adjacentType == Cell.Room || adjacentType == Cell.PlayerStart || adjacentType == Cell.StorageRoom)
                    {
                        validSpot = false;
                        break;
                    }
                }
                
                if (validSpot)
                {
                    grid[storageCell.x, storageCell.y] = Cell.StorageRoom;
                    string storageCode = GenerateRoomCode();
                    storageRooms.Add(storageCell);
                    storageRoomCodes[storageCell] = storageCode;
                    storageRoomConnectsToHallway[storageCell] = isHallway;
                    storageRoomDoorwayPositions[storageCell] = connectionCell;
                    created++;
                    Debug.Log($"Created StorageRoom at {storageCell} connected to {(isHallway ? "hallway" : "room")} at {connectionCell} with code {storageCode}");
                    break;
                }
            }
        }
        
        Debug.Log($"Created {created} storage rooms");
    }

    private void Convert1x1RoomsToStorageRooms()
    {
        Vector2Int startCell = new Vector2Int((int)playerStartRect.x, (int)playerStartRect.y);
        
        List<RectInt> roomsToRemove = new List<RectInt>();
        int convertedFromRooms = 0;
        int convertedFromHalls = 0;
        
        foreach (var rect in roomRects)
        {
            if (rect.width == 1 && rect.height == 1)
            {
                Vector2Int roomCell = new Vector2Int(rect.x, rect.y);
                
                if (IsInHardBlockZone(roomCell))
                {
                    Debug.Log($"Skipping 1x1 room at {roomCell} - in hard block zone");
                    continue;
                }
                
                if (Vector2Int.Distance(roomCell, startCell) < 3f)
                {
                    Debug.Log($"Skipping 1x1 room at {roomCell} - too close to player start");
                    continue;
                }
                
                bool connectedToRoom = false;
                Vector2Int connectionCell = Vector2Int.zero;
                
                foreach (var dir in Dirs)
                {
                    Vector2Int neighbor = roomCell + dir;
                    if (!InBounds(neighbor)) continue;
                    
                    Cell neighborType = grid[neighbor.x, neighbor.y];
                    if (neighborType == Cell.Room || neighborType == Cell.PlayerStart)
                    {
                        connectedToRoom = true;
                        connectionCell = neighbor;
                        break;
                    }
                }
                
                if (connectedToRoom)
                {
                    grid[roomCell.x, roomCell.y] = Cell.StorageRoom;
                    
                    string storageCode = roomCodes.ContainsKey(rect) ? roomCodes[rect] : GenerateRoomCode();
                    
                    storageRooms.Add(roomCell);
                    storageRoomCodes[roomCell] = storageCode;
                    storageRoomConnectsToHallway[roomCell] = false;
                    storageRoomDoorwayPositions[roomCell] = connectionCell;
                    
                    roomsToRemove.Add(rect);
                    convertedFromRooms++;
                    
                    Debug.Log($"Converted 1x1 room at {roomCell} (grid type: {grid[roomCell.x, roomCell.y]}) to storage room {storageCode}, connected to room at {connectionCell}");
                }
            }
        }
        
        foreach (var rect in roomsToRemove)
        {
            roomRects.Remove(rect);
            roomCodes.Remove(rect);
        }
        
        for (int x = margin; x < width - margin; x++)
        {
            for (int y = margin; y < height - margin; y++)
            {
                Vector2Int currentCell = new Vector2Int(x, y);
                
                if (IsInHardBlockZone(currentCell))
                    continue;
                
                if (Vector2Int.Distance(currentCell, startCell) < 3f)
                    continue;
                
                if (grid[x, y] == Cell.Hall)
                {
                    int roomNeighbors = 0;
                    int hallNeighbors = 0;
                    Vector2Int roomConnectionCell = Vector2Int.zero;
                    
                    foreach (var dir in Dirs)
                    {
                        Vector2Int neighbor = currentCell + dir;
                        if (!InBounds(neighbor)) continue;
                        
                        Cell neighborType = grid[neighbor.x, neighbor.y];
                        if (neighborType == Cell.Room || neighborType == Cell.PlayerStart)
                        {
                            roomNeighbors++;
                            roomConnectionCell = neighbor;
                        }
                        else if (neighborType == Cell.Hall)
                        {
                            hallNeighbors++;
                        }
                    }
                    
                    if (roomNeighbors == 1 && hallNeighbors == 0)
                    {
                        grid[x, y] = Cell.StorageRoom;
                        
                        string storageCode = GenerateRoomCode();
                        storageRooms.Add(currentCell);
                        storageRoomCodes[currentCell] = storageCode;
                        storageRoomConnectsToHallway[currentCell] = false;
                        storageRoomDoorwayPositions[currentCell] = roomConnectionCell;
                        
                        convertedFromHalls++;
                        Debug.Log($"Converted 1x1 hallway cell at {currentCell} to storage room {storageCode}, connected to room at {roomConnectionCell}");
                    }
                }
            }
        }
        
        Debug.Log($"Converted {convertedFromRooms} 1x1 rooms and {convertedFromHalls} 1x1 hallway cells to storage rooms");
    }

    private void RemoveStorageRoomsConnectedToStart()
    {
        Vector2Int startCell = new Vector2Int((int)playerStartRect.x, (int)playerStartRect.y);
        List<Vector2Int> toRemove = new List<Vector2Int>();
        
        foreach (var storageCell in storageRooms)
        {
            if (IsInHardBlockZone(storageCell))
            {
                toRemove.Add(storageCell);
                Debug.Log($"Removing storage room at {storageCell} - in hard block zone");
                continue;
            }
            
            if (storageRoomDoorwayPositions.ContainsKey(storageCell))
            {
                Vector2Int doorwayCell = storageRoomDoorwayPositions[storageCell];
                if (grid[doorwayCell.x, doorwayCell.y] == Cell.PlayerStart || grid[doorwayCell.x, doorwayCell.y] == Cell.GenStart)
                {
                    toRemove.Add(storageCell);
                    Debug.Log($"Removing storage room at {storageCell} - connected to start area");
                }
            }
        }
        
        foreach (var storageCell in toRemove)
        {
            grid[storageCell.x, storageCell.y] = Cell.Empty;
            storageRooms.Remove(storageCell);
            storageRoomCodes.Remove(storageCell);
            storageRoomConnectsToHallway.Remove(storageCell);
            storageRoomDoorwayPositions.Remove(storageCell);
        }
        
        if (toRemove.Count > 0)
        {
            Debug.Log($"Removed {toRemove.Count} storage rooms connected to start areas");
        }
    }

    private void AddRoomsToReachTarget()
    {
        List<Vector2Int> hallEnds = new List<Vector2Int>();
        
        // Find all dead-end hallway cells
        for (int x = margin; x < width - margin; x++)
        {
            for (int y = margin; y < height - margin; y++)
            {
                if (grid[x, y] == Cell.Hall)
                {
                    int neighborCount = 0;
                    Vector2Int bestDir = Vector2Int.zero;
                    
                    foreach (var dir in Dirs)
                    {
                        Vector2Int neighbor = new Vector2Int(x, y) + dir;
                        if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] != Cell.Empty)
                        {
                            neighborCount++;
                        }
                        else if (InBounds(neighbor))
                        {
                            bestDir = dir;
                        }
                    }
                    
                    // Dead end with empty space to expand
                    if (neighborCount == 1 && bestDir != Vector2Int.zero)
                    {
                        hallEnds.Add(new Vector2Int(x, y));
                    }
                }
            }
        }
        
        // Shuffle for variety
        Shuffle(hallEnds);
        
        // Try to add rooms at dead ends with no buffer (0) to allow closer placement
        foreach (var endPoint in hallEnds)
        {
            if (roomCount >= targetRoomCount) break;
            
            // Try all directions
            foreach (var dir in Dirs)
            {
                if (roomCount >= targetRoomCount) break;
                
                Vector2Int neighbor = endPoint + dir;
                if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] == Cell.Empty)
                {
                    if (TryMakeRoomAtEndpoint(endPoint, dir, 0))
                    {
                        roomCount++;
                        break;
                    }
                }
            }
        }
    }

    private bool TryMakeRoomAtEndpoint(Vector2Int endpoint, Vector2Int outwardDir, int bufferOverride)
    {
        int w = rng.Next(roomSize.x, roomSize.y + 1);
        int h = rng.Next(roomSize.x, roomSize.y + 1);
        
        // Room center positioned ahead of endpoint
        Vector2Int roomCenter = endpoint + outwardDir * ((w + h) / 4 + 2);
        RectInt rect = new RectInt(roomCenter.x - w / 2, roomCenter.y - h / 2, w, h);

        if (!RectInBounds(rect)) return false;
        
        // Check area is clear, but allow the endpoint hallway to exist
        if (!RectAreaClearWithBuffer(rect, bufferOverride, endpoint)) return false;

        // 1. Carve the Room
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                grid[x, y] = Cell.Room;
            }
        }
        
        string roomCode = GenerateRoomCode();
        roomRects.Add(rect);
        roomCodes[rect] = roomCode;

        // 2. Connect the entrance hallway to the room
        Vector2Int entry = ClampPointToRect(endpoint + outwardDir, rect);
        grid[entry.x, entry.y] = Cell.Room;
        
        // Carve connector path from endpoint to entry if needed
        Vector2Int current = endpoint;
        while (current != entry)
        {
            if (current.x != entry.x) current.x += (entry.x > current.x) ? 1 : -1;
            else if (current.y != entry.y) current.y += (entry.y > current.y) ? 1 : -1;
            
            if (InBounds(current) && grid[current.x, current.y] == Cell.Empty)
            {
                grid[current.x, current.y] = Cell.Hall;
            }
        }
        
        int currentDoors = 1;

        // 3. Find potential additional door slots
        List<Vector2Int[]> possibleSlots = new List<Vector2Int[]>();
        foreach (var dir in Dirs)
        {
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    Vector2Int inside = new Vector2Int(x, y);
                    if (IsOnEdge(inside, rect))
                    {
                        Vector2Int outside = inside + dir;
                        if (InBounds(outside) && grid[outside.x, outside.y] == Cell.Empty)
                        {
                            possibleSlots.Add(new Vector2Int[] { inside, outside, dir });
                        }
                    }
                }
            }
        }

        // Shuffle and pick remaining doors until we hit the random limit
        int doorLimit = rng.Next(minRoomDoors, maxRoomDoors + 1);
        Shuffle(possibleSlots);

        foreach (var slot in possibleSlots)
        {
            if (currentDoors >= doorLimit) break;

            Vector2Int outside = slot[1];
            Vector2Int dir = slot[2];

            // Ensure we aren't creating a door right next to the entrance
            if (Vector2Int.Distance(slot[0], entry) < 3) continue;

            grid[outside.x, outside.y] = Cell.Hall;
            frontier.Add(new Endpoint(outside, dir));
            currentDoors++;
        }

        return true;
    }

    private bool IsOnEdge(Vector2Int p, RectInt r)
    {
        return p.x == r.xMin || p.x == r.xMax - 1 || p.y == r.yMin || p.y == r.yMax - 1;
    }

    private int PickEndpointIndex()
    {
        if (frontier.Count <= 1) return 0;
        
        // Bias: Find the endpoint furthest from center to prevent "clumping" in the middle
        int bestIdx = 0;
        float maxScore = -1;
        for (int i = 0; i < frontier.Count; i++)
        {
            float dist = Vector2Int.Distance(frontier[i].cell, new Vector2Int(width / 2, height / 2));
            float score = dist * exploreDrive + (float)rng.NextDouble() * 10f;
            if (score > maxScore)
            {
                maxScore = score;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = rng.Next(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private bool TryExtendEndpoint(ref Endpoint ep)
    {
        Vector2Int next = ep.cell + ep.dir;
        if (!InBounds(next)) return false;
        if (grid[next.x, next.y] != Cell.Empty) return false;

        SetCell(next, Cell.Hall);
        ep.cell = next;
        ep.straight++;

        if (ep.straight >= minStraightBeforeTurn && rng.NextDouble() < turnChance)
        {
            ep.dir = (rng.NextDouble() < 0.5) ? TurnLeft(ep.dir) : TurnRight(ep.dir);
            ep.straight = 0;
        }

        return true;
    }

    private void SetCell(Vector2Int p, Cell type) { if (InBounds(p)) grid[p.x, p.y] = type; }
    private bool InBounds(Vector2Int p) => p.x >= margin && p.x < width - margin && p.y >= margin && p.y < height - margin;
    private bool RectInBounds(RectInt r) => InBounds(new Vector2Int(r.xMin, r.yMin)) && InBounds(new Vector2Int(r.xMax - 1, r.yMax - 1));
    
    private bool RectAreaClearWithBuffer(RectInt rect, int buffer, Vector2Int allowedCell)
    {
        for (int x = rect.xMin - buffer; x < rect.xMax + buffer; x++)
        {
            for (int y = rect.yMin - buffer; y < rect.yMax + buffer; y++)
            {
                Vector2Int checkPos = new Vector2Int(x, y);
                // Allow the entrance hallway cell
                if (checkPos == allowedCell) continue;
                
                if (x >= 0 && x < width && y >= 0 && y < height && grid[x, y] != Cell.Empty) 
                    return false;
            }
        }
        return true;
    }
    private Vector2Int ClampPointToRect(Vector2Int p, RectInt r) => new Vector2Int(Mathf.Clamp(p.x, r.xMin, r.xMax - 1), Mathf.Clamp(p.y, r.yMin, r.yMax - 1));
    private Vector2Int TurnLeft(Vector2Int d) => new Vector2Int(d.y, -d.x);
    private Vector2Int TurnRight(Vector2Int d) => new Vector2Int(-d.y, d.x);
    private void ClearGenerated() { for (int i = generatedRoot.childCount - 1; i >= 0; i--) DestroyImmediate(generatedRoot.GetChild(i).gameObject); }
    private Vector3 CellToWorld(Vector2Int cell) => new Vector3((cell.x - width / 2f) * cellSize, 0, (cell.y - height / 2f) * cellSize);
    
    private bool IsInHardBlockZone(Vector2Int cell)
    {
        if (Vector2Int.Distance(cell, genStartCell) < hardBlockRadius)
            return true;
        
        if (playerStartRect.width > 0)
        {
            Vector2Int playerStartCenter = new Vector2Int((int)playerStartRect.center.x, (int)playerStartRect.center.y);
            if (Vector2Int.Distance(cell, playerStartCenter) < hardBlockRadius)
                return true;
        }
        
        return false;
    }
    
    private void PlacePlayerStart()
    {
        if (endpoints.Count == 0)
        {
            Debug.LogWarning("No endpoints found for player start placement!");
            CreateDedicatedPlayerStartPath();
            return;
        }
        
        Debug.Log($"Trying to place player start at {endpoints.Count} endpoints");
        
        Shuffle(endpoints);
        
        int attemptCount = 0;
        foreach (var endpoint in endpoints)
        {
            attemptCount++;
            
            if (grid[endpoint.x, endpoint.y] == Cell.Room || grid[endpoint.x, endpoint.y] == Cell.PlayerStart)
                continue;
            
            Vector2Int dir = Vector2Int.zero;
            foreach (var d in Dirs)
            {
                Vector2Int neighbor = endpoint + d;
                if (InBounds(neighbor) && (grid[neighbor.x, neighbor.y] == Cell.Hall || grid[neighbor.x, neighbor.y] == Cell.GenStart))
                {
                    dir = -d;
                    break;
                }
            }
            
            if (dir == Vector2Int.zero) continue;
            
            Vector2Int roomCenter = endpoint + dir * 3;
            RectInt rect = new RectInt(roomCenter.x - 2, roomCenter.y - 2, 4, 4);
            
            if (!RectInBounds(rect)) continue;
            
            if (!RectAreaClearWithBuffer(rect, 3, endpoint))
                continue;
            
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    grid[x, y] = Cell.PlayerStart;
                }
            }
            
            Vector2Int entry = ClampPointToRect(endpoint + dir, rect);
            grid[entry.x, entry.y] = Cell.PlayerStart;
            
            Vector2Int current = endpoint;
            while (current != entry)
            {
                if (current.x != entry.x) current.x += (entry.x > current.x) ? 1 : -1;
                else if (current.y != entry.y) current.y += (entry.y > current.y) ? 1 : -1;
                
                if (InBounds(current) && grid[current.x, current.y] == Cell.Empty)
                {
                    grid[current.x, current.y] = Cell.Hall;
                }
            }
            
            playerStartCell = roomCenter;
            playerStartRect = rect;
            Debug.Log($"Player start placed at {roomCenter} after {attemptCount} attempts. Wall height: {playerStartRoomHeight}");
            return;
        }
        
        Debug.LogWarning($"Failed to place player start after checking {attemptCount} endpoints! Creating dedicated path...");
        CreateDedicatedPlayerStartPath();
    }
    
    private void CreateDedicatedPlayerStartPath()
    {
        List<Vector2Int> edgeHallways = new List<Vector2Int>();
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == Cell.Hall || grid[x, y] == Cell.GenStart)
                {
                    bool isNearEdge = x < 10 || x > width - 10 || y < 10 || y > height - 10;
                    if (isNearEdge)
                    {
                        edgeHallways.Add(new Vector2Int(x, y));
                    }
                }
            }
        }
        
        if (edgeHallways.Count == 0)
        {
            Debug.LogError("No hallways found to create dedicated player start path!");
            generationFailed = true;
            return;
        }
        
        Shuffle(edgeHallways);
        
        foreach (var startCell in edgeHallways)
        {
            Vector2Int[] possibleDirections = new Vector2Int[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };
            
            foreach (var direction in possibleDirections)
            {
                bool success = TryCreatePathInDirection(startCell, direction);
                if (success)
                {
                    Debug.Log($"Created dedicated player start path from {startCell} in direction {direction}");
                    return;
                }
            }
        }
        
        Debug.LogError("Failed to create dedicated player start path in any direction!");
        generationFailed = true;
    }
    
    private bool TryCreatePathInDirection(Vector2Int startCell, Vector2Int direction)
    {
        List<Vector2Int> pathCells = new List<Vector2Int>();
        
        for (int pathLength = 10; pathLength <= 30; pathLength++)
        {
            pathCells.Clear();
            
            for (int i = 1; i <= pathLength; i++)
            {
                Vector2Int pathCell = startCell + direction * i;
                if (!InBounds(pathCell))
                    return false;
                
                pathCells.Add(pathCell);
            }
            
            Vector2Int endpoint = startCell + direction * pathLength;
            Vector2Int roomCenter = endpoint + direction * 3;
            RectInt rect = new RectInt(roomCenter.x - 2, roomCenter.y - 2, 4, 4);
            
            if (!RectInBounds(rect))
                continue;
            
            bool pathClear = true;
            foreach (var cell in pathCells)
            {
                if (grid[cell.x, cell.y] != Cell.Empty && grid[cell.x, cell.y] != Cell.Hall)
                {
                    pathClear = false;
                    break;
                }
            }
            
            if (!pathClear)
                continue;
            
            if (RectAreaClearWithBuffer(rect, 3, endpoint))
            {
                foreach (var cell in pathCells)
                {
                    if (grid[cell.x, cell.y] == Cell.Empty)
                        grid[cell.x, cell.y] = Cell.Hall;
                }
                
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    for (int y = rect.yMin; y < rect.yMax; y++)
                    {
                        grid[x, y] = Cell.PlayerStart;
                    }
                }
                
                Vector2Int entry = ClampPointToRect(endpoint + direction, rect);
                Vector2Int current = endpoint;
                while (current != entry)
                {
                    if (current.x != entry.x) current.x += (entry.x > current.x) ? 1 : -1;
                    else if (current.y != entry.y) current.y += (entry.y > current.y) ? 1 : -1;
                    
                    if (InBounds(current) && grid[current.x, current.y] == Cell.Empty)
                    {
                        grid[current.x, current.y] = Cell.Hall;
                    }
                }
                
                playerStartCell = roomCenter;
                playerStartRect = rect;
                Debug.Log($"Path length {pathLength} worked! Player start at {roomCenter}. Wall height: {playerStartRoomHeight}");
                return true;
            }
        }
        
        return false;
    }
    
    private void PlaceEndRoof()
    {
        if (endpoints.Count == 0) return;
        
        Shuffle(endpoints);
        
        foreach (var endpoint in endpoints)
        {
            RectInt endRoofRect = new RectInt(endpoint.x - 2, endpoint.y - 2, 4, 4);
            
            if (!RectInBounds(endRoofRect)) continue;
            
            bool overlapsExisting = false;
            for (int x = endRoofRect.xMin; x < endRoofRect.xMax; x++)
            {
                for (int y = endRoofRect.yMin; y < endRoofRect.yMax; y++)
                {
                    if (grid[x, y] == Cell.Exit || grid[x, y] == Cell.PlayerStart) 
                    {
                        overlapsExisting = true;
                        break;
                    }
                }
                if (overlapsExisting) break;
            }
            
            if (!overlapsExisting)
            {
                for (int x = endRoofRect.xMin; x < endRoofRect.xMax; x++)
                {
                    for (int y = endRoofRect.yMin; y < endRoofRect.yMax; y++)
                    {
                        if (grid[x, y] == Cell.Empty)
                            grid[x, y] = Cell.Hall;
                    }
                }
                
                endCell = new Vector2Int(endpoint.x, endpoint.y);
                grid[endCell.x, endCell.y] = Cell.Exit;
                return;
            }
        }
    }
    
    private void ProcessRoomDoorways()
    {
        roomDoorways.Clear();
        
        foreach (var rect in roomRects)
        {
            List<Vector2Int> perimeterOpenings = new List<Vector2Int>();
            
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    bool isPerimeter = (x == rect.xMin || x == rect.xMax - 1 || y == rect.yMin || y == rect.yMax - 1);
                    if (!isPerimeter) continue;
                    
                    Vector2Int roomCell = new Vector2Int(x, y);
                    
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int neighbor = roomCell + Dirs[d];
                        if (!InBounds(neighbor)) continue;
                        
                        if (grid[neighbor.x, neighbor.y] != Cell.Empty && grid[neighbor.x, neighbor.y] != Cell.Wall)
                        {
                            perimeterOpenings.Add(roomCell);
                            break;
                        }
                    }
                }
            }
            
            List<List<Vector2Int>> doorwayGroups = GroupAdjacentDoors(perimeterOpenings);
            
            if (doorwayGroups.Count > maxRoomDoors)
            {
                doorwayGroups = doorwayGroups.OrderBy(g => g.Count).ToList();
                
                for (int i = maxRoomDoors; i < doorwayGroups.Count; i++)
                {
                    foreach (var cell in doorwayGroups[i])
                    {
                        grid[cell.x, cell.y] = Cell.Wall;
                    }
                }
                
                Debug.Log($"Room at {rect.center}: Found {doorwayGroups.Count} doorways, blocked {doorwayGroups.Count - maxRoomDoors}");
            }
            
            foreach (var cell in perimeterOpenings)
            {
                if (grid[cell.x, cell.y] != Cell.Wall)
                {
                    if (!roomDoorways.ContainsKey(cell))
                        roomDoorways[cell] = new HashSet<Vector2Int>();
                    
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int neighbor = cell + Dirs[d];
                        if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] != Cell.Empty && grid[neighbor.x, neighbor.y] != Cell.Wall)
                        {
                            roomDoorways[cell].Add(neighbor);
                        }
                    }
                }
            }
        }
        
        if (playerStartRect.width > 0)
        {
            ProcessSingleRoomDoorways(playerStartRect);
        }
    }
    
    private void ProcessSingleRoomDoorways(RectInt rect)
    {
        List<Vector2Int> perimeterOpenings = new List<Vector2Int>();
        
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                bool isPerimeter = (x == rect.xMin || x == rect.xMax - 1 || y == rect.yMin || y == rect.yMax - 1);
                if (!isPerimeter) continue;
                
                Vector2Int roomCell = new Vector2Int(x, y);
                
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int neighbor = roomCell + Dirs[d];
                    if (!InBounds(neighbor)) continue;
                    
                    if (grid[neighbor.x, neighbor.y] != Cell.Empty && grid[neighbor.x, neighbor.y] != Cell.Wall)
                    {
                        perimeterOpenings.Add(roomCell);
                        break;
                    }
                }
            }
        }
        
        List<List<Vector2Int>> doorwayGroups = GroupAdjacentDoors(perimeterOpenings);
        
        if (doorwayGroups.Count > 1)
        {
            doorwayGroups = doorwayGroups.OrderByDescending(g => g.Count).ToList();
            
            for (int i = 1; i < doorwayGroups.Count; i++)
            {
                foreach (var cell in doorwayGroups[i])
                {
                    grid[cell.x, cell.y] = Cell.Wall;
                }
            }
            
            Debug.Log($"Player start room: Found {doorwayGroups.Count} doorways, blocked {doorwayGroups.Count - 1}");
        }
        
        foreach (var cell in perimeterOpenings)
        {
            if (grid[cell.x, cell.y] == Cell.PlayerStart)
            {
                if (!roomDoorways.ContainsKey(cell))
                    roomDoorways[cell] = new HashSet<Vector2Int>();
                
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int neighbor = cell + Dirs[d];
                    if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] != Cell.Empty && grid[neighbor.x, neighbor.y] != Cell.Wall)
                    {
                        roomDoorways[cell].Add(neighbor);
                    }
                }
            }
        }
    }
    
    private List<List<Vector2Int>> GroupAdjacentDoors(List<Vector2Int> doors)
    {
        List<List<Vector2Int>> groups = new List<List<Vector2Int>>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        
        foreach (var door in doors)
        {
            if (visited.Contains(door))
                continue;
            
            List<Vector2Int> group = new List<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(door);
            visited.Add(door);
            
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                group.Add(current);
                
                foreach (var dir in Dirs)
                {
                    Vector2Int neighbor = current + dir;
                    if (doors.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
            
            groups.Add(group);
        }
        
        return groups;
    }
    
    private void BuildGeometry()
    {
        if (generatedRoot != null)
        {
            foreach (Transform child in generatedRoot)
            {
                DestroyImmediate(child.gameObject);
            }
        }
        
        floorCounter = 0;
        wallCounter = 0;
        upperWallCounter = 0;
        roofCounter = 0;
        
        Dictionary<string, int> roomPartCounts = new Dictionary<string, int>();
        
        int playerStartCellsFound = 0;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == Cell.Empty) continue;
                
                Vector3 pos = CellToWorld(new Vector2Int(x, y));
                Vector2Int currentCell = new Vector2Int(x, y);
                bool isRoom = grid[x, y] == Cell.Room;
                bool isPlayerStart = grid[x, y] == Cell.PlayerStart;
                bool isExit = grid[x, y] == Cell.Exit;
                bool isWall = grid[x, y] == Cell.Wall;
                bool isStorageRoom = grid[x, y] == Cell.StorageRoom;
                
                string roomCode = null;
                if (isPlayerStart)
                {
                    roomCode = "PlayerStart";
                }
                else if (isStorageRoom)
                {
                    if (storageRoomCodes.ContainsKey(currentCell))
                    {
                        roomCode = "StorageRoom_" + storageRoomCodes[currentCell];
                    }
                    else
                    {
                        Debug.LogWarning($"StorageRoom cell at {currentCell} found in grid but missing from storageRoomCodes dictionary!");
                    }
                }
                else if (isRoom || isWall)
                {
                    foreach (var rect in roomRects)
                    {
                        if (rect.Contains(currentCell))
                        {
                            if (roomCodes.ContainsKey(rect))
                                roomCode = roomCodes[rect];
                            break;
                        }
                    }
                }
                
                if (isPlayerStart) playerStartCellsFound++;
                
                float currentWallHeight = wallHeight;
                if (isPlayerStart)
                    currentWallHeight = playerStartRoomHeight;
                else if (isRoom)
                    currentWallHeight = wallHeight * 2.0f;
                else if (isStorageRoom)
                    currentWallHeight = wallHeight;
                
                if (!isWall)
                {
                    string floorName = roomCode != null ? $"Floor_{roomCode}" : "Floor";
                    float floorYPos = isPlayerStart ? -currentWallHeight - floorThickness / 2 : -floorThickness / 2;
                    GameObject floor = CreateBox(floorName, pos + Vector3.up * floorYPos, new Vector3(cellSize, floorThickness, cellSize));
                    
                    if (isPlayerStart)
                    {
                        ApplyBlackFadeToFloor(floor, pos);
                    }
                    
                    if (generateRoofs)
                    {
                        float roofYPos = currentWallHeight + roofThickness / 2;
                        string roofName = roomCode != null ? $"Roof_{roomCode}" : "Roof";
                        GameObject roof = CreateBox(roofName, pos + Vector3.up * roofYPos, new Vector3(cellSize, roofThickness, cellSize));
                        
                        if (isPlayerStart)
                        {
                            ApplyBlackFadeToFloor(roof, pos);
                        }
                    }
                }
                
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int neighbor = new Vector2Int(x, y) + Dirs[d];
                    
                    bool shouldPlaceWall = true;
                    
                    if (isWall)
                    {
                        shouldPlaceWall = true;
                    }
                    else if (InBounds(neighbor))
                    {
                        bool neighborIsWall = grid[neighbor.x, neighbor.y] == Cell.Wall;
                        
                        if (neighborIsWall)
                        {
                            shouldPlaceWall = true;
                        }
                        else if (isRoom || isPlayerStart)
                        {
                            bool neighborIsRoom = grid[neighbor.x, neighbor.y] == Cell.Room || 
                                                 grid[neighbor.x, neighbor.y] == Cell.PlayerStart;
                            bool neighborIsEmpty = grid[neighbor.x, neighbor.y] == Cell.Empty;
                            
                            if (neighborIsRoom)
                            {
                                shouldPlaceWall = false;
                            }
                            else if (neighborIsEmpty)
                            {
                                shouldPlaceWall = true;
                            }
                            else
                            {
                                shouldPlaceWall = false;
                            }
                        }
                        else if (isStorageRoom)
                        {
                            bool neighborIsEmpty = grid[neighbor.x, neighbor.y] == Cell.Empty;
                            bool isDesignatedDoorway = false;
                            
                            if (storageRoomDoorwayPositions.ContainsKey(currentCell))
                            {
                                Vector2Int doorwayCell = storageRoomDoorwayPositions[currentCell];
                                isDesignatedDoorway = (neighbor == doorwayCell);
                            }
                            
                            if (neighborIsEmpty)
                            {
                                shouldPlaceWall = true;
                            }
                            else if (isDesignatedDoorway)
                            {
                                shouldPlaceWall = false;
                            }
                            else
                            {
                                shouldPlaceWall = true;
                            }
                        }
                        else if (grid[neighbor.x, neighbor.y] == Cell.Empty)
                        {
                            shouldPlaceWall = true;
                        }
                        else
                        {
                            shouldPlaceWall = false;
                        }
                    }
                    else
                    {
                        shouldPlaceWall = true;
                    }
                    
                    if (shouldPlaceWall)
                    {
                        Vector3 wallPos = pos + new Vector3(Dirs[d].x * cellSize / 2f, currentWallHeight / 2f, Dirs[d].y * cellSize / 2f);
                        Vector3 wallSize = (Dirs[d].x != 0) ? new Vector3(wallThickness, currentWallHeight, cellSize) : new Vector3(cellSize, currentWallHeight, wallThickness);
                        
                        string wallName = roomCode != null ? $"Wall_{roomCode}" : "Wall";
                        CreateBox(wallName, wallPos, wallSize);
                        
                        if (isPlayerStart)
                        {
                            Vector3 lowerWallPos = pos + new Vector3(Dirs[d].x * cellSize / 2f, -currentWallHeight / 2f, Dirs[d].y * cellSize / 2f);
                            Vector3 lowerWallSize = wallSize;
                            string lowerWallName = roomCode != null ? $"LowerWall_{roomCode}" : "LowerWall";
                            CreateBox(lowerWallName, lowerWallPos, lowerWallSize);
                        }
                    }
                    
                    if (InBounds(neighbor) && !shouldPlaceWall && !isWall)
                    {
                        bool neighborIsRoom = grid[neighbor.x, neighbor.y] == Cell.Room;
                        bool neighborIsPlayerStart = grid[neighbor.x, neighbor.y] == Cell.PlayerStart;
                        
                        float neighborWallHeight = wallHeight;
                        if (neighborIsPlayerStart)
                            neighborWallHeight = playerStartRoomHeight;
                        else if (neighborIsRoom)
                            neighborWallHeight = wallHeight * 2.0f;
                        
                        if (currentWallHeight > neighborWallHeight)
                        {
                            float upperWallYPos = neighborWallHeight + upperWallHeight / 2f;
                            Vector3 upperWallPos = pos + new Vector3(Dirs[d].x * cellSize / 2f, upperWallYPos, Dirs[d].y * cellSize / 2f);
                            Vector3 upperWallSize = (Dirs[d].x != 0) ? new Vector3(wallThickness, upperWallHeight, cellSize) : new Vector3(cellSize, upperWallHeight, wallThickness);
                            
                            string upperWallName = roomCode != null ? $"UpperWall_{roomCode}" : "UpperWall";
                            
                            if (roomCode == "PlayerStart" && playerStartUpperWallPrefab != null)
                            {
                                CreatePlayerStartWall(upperWallName, upperWallPos, upperWallSize, playerStartUpperWallPrefab);
                            }
                            else
                            {
                                CreateBox(upperWallName, upperWallPos, upperWallSize);
                            }
                        }
                        
                        if (isPlayerStart && !neighborIsPlayerStart && !useCustomWallSize)
                        {
                            Vector3 lowerDoorWallPos = pos + new Vector3(Dirs[d].x * cellSize / 2f, -currentWallHeight / 2f, Dirs[d].y * cellSize / 2f);
                            Vector3 lowerDoorWallSize = (Dirs[d].x != 0) ? new Vector3(wallThickness, currentWallHeight, cellSize) : new Vector3(cellSize, currentWallHeight, wallThickness);
                            
                            string lowerDoorWallName = roomCode != null ? $"LowerDoorWall_{roomCode}" : "LowerDoorWall";
                            CreateBox(lowerDoorWallName, lowerDoorWallPos, lowerDoorWallSize);
                        }
                    }
                }
            }
        }
        
        Debug.Log($"Built geometry with {playerStartCellsFound} player start cells. playerStartRoomHeight = {playerStartRoomHeight}");
        
        if (useCustomWallSize)
        {
            ReplacePlayerStartWallsWithCustom();
        }
        
        BuildEndRoof();
    }
    
    private void BuildEndRoof()
    {
        if (endCell == Vector2Int.zero) return;
        
        Vector3 centerPos = CellToWorld(endCell);
        RectInt endRoofRect = new RectInt(endCell.x - 2, endCell.y - 2, 4, 4);
        
        for (int x = endRoofRect.xMin; x < endRoofRect.xMax; x++)
        {
            for (int y = endRoofRect.yMin; y < endRoofRect.yMax; y++)
            {
                if (!InBounds(new Vector2Int(x, y))) continue;
                
                bool isRoom = grid[x, y] == Cell.Room;
                bool isPlayerStart = grid[x, y] == Cell.PlayerStart;
                
                float currentWallHeight = wallHeight;
                if (isPlayerStart)
                    currentWallHeight = playerStartRoomHeight;
                else if (isRoom)
                    currentWallHeight = wallHeight * 2.0f;
                
                Vector3 cellWorldPos = CellToWorld(new Vector2Int(x, y));
                
                if (generateRoofs)
                {
                    CreateBox("EndRoof", cellWorldPos + Vector3.up * (currentWallHeight + roofThickness / 2), new Vector3(cellSize, roofThickness, cellSize));
                }
            }
        }
    }

    private int floorCounter = 0;
    private int wallCounter = 0;
    private int upperWallCounter = 0;
    private int roofCounter = 0;
    
    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        char[] code = new char[8];
        for (int i = 0; i < 8; i++)
        {
            code[i] = chars[rng.Next(chars.Length)];
        }
        return new string(code);
    }
    
    private GameObject CreateBox(string name, Vector3 pos, Vector3 size)
    {
        GameObject prefab = null;
        
        if (name.StartsWith("Floor"))
            prefab = floorPrefab;
        else if (name.StartsWith("Wall") || name.StartsWith("UpperWall") || name.StartsWith("LowerWall"))
            prefab = wallPrefab;
        else if (name.StartsWith("Roof"))
            prefab = roofPrefab;
        
        GameObject go;
        
        if (prefab != null)
        {
            go = Instantiate(prefab, generatedRoot);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(generatedRoot);
            go.transform.position = pos;
            go.transform.localScale = size;
        }
        
        return go;
    }
    
    private GameObject CreatePlayerStartWall(string name, Vector3 pos, Vector3 size, GameObject customPrefab)
    {
        GameObject go;
        
        if (customPrefab != null)
        {
            go = Instantiate(customPrefab, generatedRoot);
            go.name = name;
            go.transform.position = pos;
            
            Collider collider = go.GetComponent<Collider>();
            Renderer renderer = go.GetComponent<Renderer>();
            
            if (collider != null)
            {
                Bounds bounds = collider.bounds;
                Vector3 prefabSize = bounds.size;
                
                Vector3 scaleMultiplier = new Vector3(
                    size.x / prefabSize.x,
                    size.y / prefabSize.y,
                    size.z / prefabSize.z
                );
                
                go.transform.localScale = Vector3.Scale(go.transform.localScale, scaleMultiplier);
                Debug.Log($"Created {name} from prefab using COLLIDER bounds: desired size {size}, prefab collider size {prefabSize}, scale multiplier {scaleMultiplier}");
            }
            else if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                Vector3 prefabSize = bounds.size;
                
                Vector3 scaleMultiplier = new Vector3(
                    size.x / prefabSize.x,
                    size.y / prefabSize.y,
                    size.z / prefabSize.z
                );
                
                go.transform.localScale = Vector3.Scale(go.transform.localScale, scaleMultiplier);
                Debug.Log($"Created {name} from prefab using RENDERER bounds: desired size {size}, prefab size {prefabSize}, scale multiplier {scaleMultiplier}");
            }
            else
            {
                go.transform.localScale = size;
            }
        }
        else
        {
            go = CreateBox(name, pos, size);
        }
        
        return go;
    }
    
    private bool IsWallNextToDoorway(Vector2Int currentCell, Vector2Int neighbor)
    {
        if (!InBounds(neighbor))
            return false;
        
        Cell neighborCell = grid[neighbor.x, neighbor.y];
        
        if (neighborCell == Cell.Room || neighborCell == Cell.PlayerStart || neighborCell == Cell.Hall)
        {
            foreach (var dir in Dirs)
            {
                Vector2Int adjacentToNeighbor = neighbor + dir;
                if (InBounds(adjacentToNeighbor))
                {
                    Cell adjacentCell = grid[adjacentToNeighbor.x, adjacentToNeighbor.y];
                    
                    if (adjacentCell == Cell.Room || adjacentCell == Cell.PlayerStart || adjacentCell == Cell.Hall)
                    {
                        if (adjacentCell != neighborCell)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        
        return false;
    }
    
    private string GetClosestSide(Vector3 p, float minX, float maxX, float minZ, float maxZ)
    {
        float dWest = Mathf.Abs(p.x - minX);
        float dEast = Mathf.Abs(p.x - maxX);
        float dSouth = Mathf.Abs(p.z - minZ);
        float dNorth = Mathf.Abs(p.z - maxZ);
        
        float best = dWest;
        string side = "West";
        
        if (dEast < best) { best = dEast; side = "East"; }
        if (dSouth < best) { best = dSouth; side = "South"; }
        if (dNorth < best) { best = dNorth; side = "North"; }
        
        return side;
    }
    
    private void ReplacePlayerStartWallsWithCustom()
    {
        List<Transform> wallsToRemove = new List<Transform>();
        List<Transform> upperWalls = new List<Transform>();
        List<Transform> floorTiles = new List<Transform>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Wall_PlayerStart" || child.name == "LowerWall_PlayerStart")
            {
                wallsToRemove.Add(child);
            }
            else if (child.name == "UpperWall_PlayerStart")
            {
                upperWalls.Add(child);
            }
            else if (child.name == "Floor_PlayerStart")
            {
                floorTiles.Add(child);
            }
        }
        
        if (floorTiles.Count == 0)
        {
            Debug.LogWarning("No Floor_PlayerStart tiles found!");
            return;
        }
        
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        
        foreach (Transform floor in floorTiles)
        {
            Vector3 pos = floor.position;
            Vector3 scale = floor.localScale;
            
            float halfWidth = scale.x / 2f;
            float halfDepth = scale.z / 2f;
            
            minX = Mathf.Min(minX, pos.x - halfWidth);
            maxX = Mathf.Max(maxX, pos.x + halfWidth);
            minZ = Mathf.Min(minZ, pos.z - halfDepth);
            maxZ = Mathf.Max(maxZ, pos.z + halfDepth);
        }
        
        Dictionary<string, Transform> upperWallBySide = new Dictionary<string, Transform>();
        Dictionary<string, Vector3> doorPositionBySide = new Dictionary<string, Vector3>();
        
        foreach (Transform upperWall in upperWalls)
        {
            string side = GetClosestSide(upperWall.position, minX, maxX, minZ, maxZ);
            upperWallBySide[side] = upperWall;
            doorPositionBySide[side] = upperWall.position;
        }
        
        foreach (var kvp in upperWallBySide)
        {
            string side = kvp.Key;
            Transform upperWall = kvp.Value;
            float oldHeight = upperWall.localScale.y;
            upperWall.localScale = new Vector3(9f, upperWallHeight, 6f);
            
            Vector3 pos = upperWall.position;
            float heightDiff = oldHeight - upperWallHeight;
            pos.y = pos.y - heightDiff / 2f + upperWallYOffset;
            upperWall.position = pos;
            
            Quaternion rotation = Quaternion.identity;
            if (side == "North")
                rotation = Quaternion.Euler(0, 180, 0);
            else if (side == "South")
                rotation = Quaternion.Euler(0, 0, 0);
            else if (side == "East")
                rotation = Quaternion.Euler(0, -90, 0);
            else if (side == "West")
                rotation = Quaternion.Euler(0, 90, 0);
            
            upperWall.rotation = rotation;
        }
        
        foreach (var kvp in upperWallBySide)
        {
            string side = kvp.Key;
            Transform upperWall = kvp.Value;
            
            Vector3 pos = upperWall.position;
            if (side == "North" || side == "South")
            {
                pos.x += upperWallLateralOffset;
            }
            else if (side == "East" || side == "West")
            {
                pos.z += upperWallLateralOffset;
            }
            upperWall.position = pos;
        }
        
        float wallHeight = playerStartRoomHeight;
        int numVerticalStacks = Mathf.CeilToInt(wallHeight / customWallSize.y);
        
        string[] sides = { "North", "South", "East", "West" };
        
        foreach (string side in sides)
        {
            bool hasDoor = doorPositionBySide.ContainsKey(side);
            
            Quaternion rotation = Quaternion.identity;
            if (side == "North")
                rotation = Quaternion.Euler(0, 180, 0);
            else if (side == "South")
                rotation = Quaternion.Euler(0, 0, 0);
            else if (side == "East")
                rotation = Quaternion.Euler(0, -90, 0);
            else if (side == "West")
                rotation = Quaternion.Euler(0, 90, 0);
            
            float sideCoord = 0f;
            Vector3 wallPos1 = Vector3.zero;
            Vector3 wallPos2 = Vector3.zero;
            
            if (side == "North")
            {
                sideCoord = maxZ;
                wallPos1 = new Vector3(minX + customWallSize.x / 2f, 0, sideCoord);
                wallPos2 = new Vector3(maxX - customWallSize.x / 2f, 0, sideCoord);
            }
            else if (side == "South")
            {
                sideCoord = minZ;
                wallPos1 = new Vector3(minX + customWallSize.x / 2f, 0, sideCoord);
                wallPos2 = new Vector3(maxX - customWallSize.x / 2f, 0, sideCoord);
            }
            else if (side == "East")
            {
                sideCoord = maxX;
                wallPos1 = new Vector3(sideCoord, 0, minZ + customWallSize.x / 2f);
                wallPos2 = new Vector3(sideCoord, 0, maxZ - customWallSize.x / 2f);
            }
            else if (side == "West")
            {
                sideCoord = minX;
                wallPos1 = new Vector3(sideCoord, 0, minZ + customWallSize.x / 2f);
                wallPos2 = new Vector3(sideCoord, 0, maxZ - customWallSize.x / 2f);
            }
            
            for (int stackIndex = 0; stackIndex < numVerticalStacks; stackIndex++)
            {
                float yOffset = stackIndex * customWallSize.y + customWallSize.y / 2f + wallYOffset;
                
                Vector3 pos1T = wallPos1;
                pos1T.y = yOffset;
                
                Vector3 pos2T = wallPos2;
                pos2T.y = yOffset;
                
                GameObject wall1T = CreatePlayerStartWall("Wall_PlayerStart", pos1T, customWallSize, playerStartWallPrefab);
                wall1T.transform.rotation = rotation;
                
                GameObject wall2T = CreatePlayerStartWall("Wall_PlayerStart", pos2T, customWallSize, playerStartWallPrefab);
                wall2T.transform.rotation = rotation;
                
                Vector3 pos1B = wallPos1;
                pos1B.y = -yOffset;
                
                Vector3 pos2B = wallPos2;
                pos2B.y = -yOffset;
                
                GameObject wall1B = CreatePlayerStartWall("Wall_PlayerStart", pos1B, customWallSize, playerStartWallPrefab);
                wall1B.transform.rotation = rotation;
                
                GameObject wall2B = CreatePlayerStartWall("Wall_PlayerStart", pos2B, customWallSize, playerStartWallPrefab);
                wall2B.transform.rotation = rotation;
            }
            
            if (hasDoor)
            {
                Transform doorTransform = upperWallBySide[side];
                Vector3 doorPos = doorTransform.position;
                Vector3 doorScale = doorTransform.localScale;
                
                float doorWidth, doorLeftEdge, doorRightEdge;
                float roomLeftEdge, roomRightEdge;
                
                if (side == "North" || side == "South")
                {
                    doorWidth = doorScale.z;
                    doorLeftEdge = doorPos.x - doorWidth / 2f;
                    doorRightEdge = doorPos.x + doorWidth / 2f;
                    roomLeftEdge = minX;
                    roomRightEdge = maxX;
                }
                else
                {
                    doorWidth = doorScale.x;
                    doorLeftEdge = doorPos.z - doorWidth / 2f;
                    doorRightEdge = doorPos.z + doorWidth / 2f;
                    roomLeftEdge = minZ;
                    roomRightEdge = maxZ;
                }
                
                float leftGapSize = doorLeftEdge - roomLeftEdge;
                float rightGapSize = roomRightEdge - doorRightEdge;
                
                float gapSize, gapCenter;
                
                gapSize = Mathf.Min(leftGapSize, rightGapSize);

                if (leftGapSize < rightGapSize)
                    gapCenter = roomLeftEdge + gapSize / 2f;
                else
                    gapCenter = roomRightEdge - gapSize / 2f;
                
                if (gapSize >= 0.5f)
                {
                    Vector3 fillPos = Vector3.zero;
                    float fillYPosition = fillWallHeight / 2f + fillWallYOffset;
                    
                    if (side == "North")
                    {
                        fillPos = new Vector3(gapCenter, fillYPosition, maxZ);
                    }
                    else if (side == "South")
                    {
                        fillPos = new Vector3(gapCenter, fillYPosition, minZ);
                    }
                    else if (side == "East")
                    {
                        fillPos = new Vector3(maxX, fillYPosition, gapCenter);
                    }
                    else if (side == "West")
                    {
                        fillPos = new Vector3(minX, fillYPosition, gapCenter);
                    }
                    
                    GameObject fillWall = CreatePlayerStartWall("FillWall_PlayerStart", fillPos, fillWallSize, playerStartFillWallPrefab);
                    fillWall.transform.rotation = rotation;
                }
            }
        }
        
        foreach (Transform wall in wallsToRemove)
        {
            DestroyImmediate(wall.gameObject);
        }
    }
    
    private void ApplyBlackFadeToFloor(GameObject floor, Vector3 worldPos)
    {
        MeshFilter meshFilter = floor.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;
        
        Mesh mesh = Instantiate(meshFilter.sharedMesh);
        Vector3[] vertices = mesh.vertices;
        Color[] colors = new Color[vertices.Length];
        
        Vector3 playerStartPos = CellToWorld(playerStartCell);
        float maxDistance = 30f;
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertexPos = floor.transform.TransformPoint(vertices[i]);
            float distance = Vector3.Distance(new Vector3(worldVertexPos.x, 0, worldVertexPos.z), 
                                             new Vector3(playerStartPos.x, 0, playerStartPos.z));
            
            float fadeAmount = Mathf.Clamp01(distance / maxDistance);
            float alpha = Mathf.Lerp(0.3f, 1f, fadeAmount);
            colors[i] = new Color(0, 0, 0, alpha);
        }
        
        mesh.colors = colors;
        meshFilter.mesh = mesh;
        
        Renderer renderer = floor.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            
            mat.SetColor("_BaseColor", Color.black);
            
            renderer.material = mat;
        }
    }
    
    private void RemoveWallsOverlappingPlayerStartDoor()
    {
        Transform playerStartDoor = null;
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Door_PlayerStart")
            {
                playerStartDoor = child;
                break;
            }
        }
        
        if (playerStartDoor == null)
        {
            Debug.LogWarning("Door_PlayerStart not found, skipping wall overlap removal");
            return;
        }
        
        GameObject overlapDetector = GameObject.CreatePrimitive(PrimitiveType.Cube);
        overlapDetector.name = "DoorOverlapDetector";
        overlapDetector.transform.SetParent(playerStartDoor);
        overlapDetector.transform.localPosition = Vector3.zero;
        overlapDetector.transform.localRotation = Quaternion.identity;
        overlapDetector.transform.localScale = new Vector3(1f, 0.1f, 1f);
        
        Renderer detectorRenderer = overlapDetector.GetComponent<Renderer>();
        if (detectorRenderer != null)
        {
            detectorRenderer.enabled = false;
        }
        
        List<Transform> allWalls = new List<Transform>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Wall_PlayerStart")
            {
                allWalls.Add(child);
            }
        }
        
        Bounds detectorBounds = new Bounds(overlapDetector.transform.position, overlapDetector.transform.lossyScale);
        
        Debug.Log($"Door overlap detector at {overlapDetector.transform.position}, bounds: center={detectorBounds.center}, size={detectorBounds.size}");
        
        List<Transform> wallsToRemove = new List<Transform>();
        
        foreach (Transform wall in allWalls)
        {
            Bounds wallBounds = GetOrientedBounds(wall);
            
            if (detectorBounds.Intersects(wallBounds))
            {
                float distance = Vector3.Distance(detectorBounds.center, wallBounds.center);
                Debug.Log($"Wall '{wall.name}' at {wall.position}, bounds center={wallBounds.center}, extents={wallBounds.extents}, distance={distance:F2} - REMOVING");
                wallsToRemove.Add(wall);
            }
        }
        
        foreach (Transform wall in wallsToRemove)
        {
            Debug.Log($"Removed wall '{wall.name}' overlapping with Door_PlayerStart center");
            DestroyImmediate(wall.gameObject);
        }
        
        DestroyImmediate(overlapDetector);
        
        Debug.Log($"Total walls removed due to Door_PlayerStart overlap: {wallsToRemove.Count}");
    }
    
    private void RemoveWallsOverlappingDoors(Dictionary<string, Transform> doorTransforms)
    {
        List<Transform> allWalls = new List<Transform>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Wall_PlayerStart" || child.name == "FillWall_PlayerStart")
            {
                allWalls.Add(child);
            }
        }
        
        foreach (Transform wall in allWalls)
        {
            Renderer renderer = wall.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
                renderer.enabled = true;
            }
        }
        
        int totalRemoved = 0;
        const float TOLERANCE = 0.1f;
        
        foreach (var kvp in doorTransforms)
        {
            Transform door = kvp.Value;
            Renderer doorRenderer = door.GetComponent<Renderer>();
            if (doorRenderer != null)
            {
                doorRenderer.enabled = false;
                doorRenderer.enabled = true;
            }
            
            Bounds doorBounds = GetOrientedBounds(door);
            doorBounds.Expand(TOLERANCE * 2f);
            
            Debug.Log($"Door '{door.name}' at {door.position} bounds: center={doorBounds.center}, extents={doorBounds.extents}, size={doorBounds.size}");
            
            List<Transform> wallsToRemove = new List<Transform>();
            
            foreach (Transform wall in allWalls)
            {
                Bounds wallBounds = GetOrientedBounds(wall);
                
                if (doorBounds.Intersects(wallBounds))
                {
                    float distance = Vector3.Distance(doorBounds.center, wallBounds.center);
                    Debug.Log($"Wall '{wall.name}' at {wall.position} bounds: center={wallBounds.center}, extents={wallBounds.extents} - distance={distance:F2} - REMOVING");
                    wallsToRemove.Add(wall);
                }
            }
            
            foreach (Transform wall in wallsToRemove)
            {
                Debug.Log($"Removed wall '{wall.name}' overlapping with door '{door.name}'");
                allWalls.Remove(wall);
                DestroyImmediate(wall.gameObject);
                totalRemoved++;
            }
        }
        
        Debug.Log($"Total walls removed due to door overlap: {totalRemoved}");
    }
    
    private Bounds GetOrientedBounds(Transform transform)
    {
        Renderer renderer = transform.GetComponent<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds;
        }
        
        Collider collider = transform.GetComponent<Collider>();
        if (collider != null)
        {
            return collider.bounds;
        }
        
        Vector3 worldScale = transform.lossyScale;
        return new Bounds(transform.position, worldScale);
    }
    
    private void PlaceDoors()
    {
        List<Transform> upperWalls = new List<Transform>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name.StartsWith("UpperWall_"))
            {
                upperWalls.Add(child);
            }
        }
        
        Debug.Log($"Placing doors at {upperWalls.Count} doorway locations");
        
        foreach (Transform upperWall in upperWalls)
        {
            string roomCode = upperWall.name.Substring("UpperWall_".Length);
            bool isStorageRoomDoor = roomCode.StartsWith("StorageRoom_");
            bool isMainDoor = roomCode == "PlayerStart";
            
            string side = "";
            if (roomCode == "PlayerStart")
            {
                List<Transform> floorTiles = new List<Transform>();
                foreach (Transform child in generatedRoot)
                {
                    if (child.name == "Floor_PlayerStart")
                    {
                        floorTiles.Add(child);
                    }
                }
                
                if (floorTiles.Count > 0)
                {
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minZ = float.MaxValue, maxZ = float.MinValue;
                    
                    foreach (Transform floor in floorTiles)
                    {
                        Vector3 pos = floor.position;
                        Vector3 scale = floor.localScale;
                        
                        float halfWidth = scale.x / 2f;
                        float halfDepth = scale.z / 2f;
                        
                        minX = Mathf.Min(minX, pos.x - halfWidth);
                        maxX = Mathf.Max(maxX, pos.x + halfWidth);
                        minZ = Mathf.Min(minZ, pos.z - halfDepth);
                        maxZ = Mathf.Max(maxZ, pos.z + halfDepth);
                    }
                    
                    side = GetClosestSide(upperWall.position, minX, maxX, minZ, maxZ);
                }
                
                bool vertical = upperWall.localScale.x < upperWall.localScale.z;
                Vector3 openingScale = vertical
                    ? new Vector3(wallThickness, wallHeight, cellSize)
                    : new Vector3(cellSize, wallHeight, wallThickness);
                
                PlaceDoorAtLocation(upperWall.position, openingScale, roomCode, isStorageRoomDoor, isMainDoor, side);
            }
            else
            {
                PlaceDoorAtLocation(upperWall.position, upperWall.localScale, roomCode, isStorageRoomDoor, isMainDoor, side);
            }
        }
        
        PlaceStorageRoomDoors();
    }
    
    private void PlaceStorageRoomDoors()
    {
        foreach (var storageCell in storageRooms)
        {
            string storageCode = storageRoomCodes[storageCell];
            
            if (!storageRoomConnectsToHallway.TryGetValue(storageCell, out bool connectsToHallway))
            {
                Debug.LogWarning($"Storage room at {storageCell} missing connection info, skipping door placement");
                continue;
            }
            
            if (!connectsToHallway)
            {
                Debug.Log($"Storage room {storageCode} at {storageCell} is connected to a room, letting room door system handle it");
                continue;
            }
            
            if (!storageRoomDoorwayPositions.TryGetValue(storageCell, out Vector2Int doorwayCell))
            {
                Debug.LogWarning($"Storage room at {storageCell} missing doorway position, skipping door placement");
                continue;
            }
            
            Vector3 hallPos = CellToWorld(doorwayCell);
            Vector3 storagePos = CellToWorld(storageCell);
            Vector3 doorPosition = (hallPos + storagePos) / 2f;
            
            Vector2Int dirToDoor = doorwayCell - storageCell;
            bool isVertical = dirToDoor.x != 0;
            Vector3 doorScale = isVertical ? new Vector3(wallThickness, wallHeight, cellSize) : new Vector3(cellSize, wallHeight, wallThickness);
            
            PlaceDoorAtLocation(doorPosition, doorScale, "StorageRoom_" + storageCode, true, false);
            Debug.Log($"Placed door for storage room {storageCode} at {storageCell} connecting to doorway at {doorwayCell}");
        }
    }
    
    private void FixStorageRoomDoorScales()
    {
        int fixedCount = 0;
        
        foreach (var kvp in storageRoomCodes)
        {
            Vector2Int storageCell = kvp.Key;
            string storageCode = kvp.Value;
            
            if (!storageRoomConnectsToHallway.TryGetValue(storageCell, out bool connectsToHallway))
            {
                continue;
            }
            
            if (connectsToHallway)
            {
                continue;
            }
            
            if (!storageRoomDoorwayPositions.TryGetValue(storageCell, out Vector2Int doorwayCell))
            {
                continue;
            }
            
            Vector3 hallPos = CellToWorld(doorwayCell);
            Vector3 storagePos = CellToWorld(storageCell);
            Vector3 doorPosition = (hallPos + storagePos) / 2f;
            
            foreach (Transform child in generatedRoot)
            {
                if (child.name.StartsWith("Door_") && !child.name.Contains("StorageRoom"))
                {
                    float distanceToDoor = Vector3.Distance(child.position, new Vector3(doorPosition.x, child.position.y, doorPosition.z));
                    
                    if (distanceToDoor < 0.5f)
                    {
                        Vector3 currentScale = child.localScale;
                        if (Mathf.Abs(currentScale.x - 4f) < 0.1f)
                        {
                            child.localScale = new Vector3(2f, currentScale.y, currentScale.z);
                            fixedCount++;
                            Debug.Log($"Fixed door scale for storage room {storageCode} - changed X from 4 to 2 on door {child.name}");
                        }
                    }
                }
            }
        }
        
        Debug.Log($"Fixed {fixedCount} storage room door scales");
    }
    
    private void PlaceDropBoxes()
    {
        if (dropBoxPrefab == null)
        {
            Debug.LogWarning("No drop box prefab assigned, skipping drop box placement");
            return;
        }
        
        PlaceMainDropBox();
        PlaceDeadEndDropBoxes();
    }
    
    private void PlaceMainDropBox()
    {
        GameObject prefabToUse = mainDropBoxPrefab != null ? mainDropBoxPrefab : dropBoxPrefab;
        
        if (prefabToUse == null)
        {
            Debug.LogWarning("No main drop box prefab assigned, skipping main drop box placement");
            return;
        }
        
        Transform doorTransform = null;
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Door_PlayerStart")
            {
                doorTransform = child;
                break;
            }
        }
        
        if (doorTransform == null)
        {
            Debug.LogWarning("Could not find Door_PlayerStart, skipping main drop box placement");
            return;
        }
        
        Transform closestWall = null;
        float closestDistance = float.MaxValue;
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Wall_PlayerStart")
            {
                float distance = Vector3.Distance(child.position, doorTransform.position);
                if (distance > 0.5f && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestWall = child;
                }
            }
        }
        
        if (closestWall == null)
        {
            Debug.LogWarning("Could not find Wall_PlayerStart next to door, skipping main drop box placement");
            return;
        }
        
        Vector3 dropBoxPosition = closestWall.position;
        dropBoxPosition.y = 2f;
        
        Quaternion dropBoxRotation = closestWall.rotation;
        
        string dropBoxCode = GenerateRoomCode();
        
        GameObject mainDropBox = Instantiate(prefabToUse, generatedRoot);
        mainDropBox.name = "DropBox_Main_" + dropBoxCode;
        mainDropBox.transform.position = dropBoxPosition;
        mainDropBox.transform.rotation = dropBoxRotation;
        
        Debug.Log($"Placed main drop box on Wall_PlayerStart next to door at {dropBoxPosition} with code {dropBoxCode}");
    }
    
    private void PlacePlayerStartPlatforms()
    {
        Transform doorFrame = null;
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "DoorFrame_PlayerStart")
            {
                doorFrame = child;
                break;
            }
        }
        
        if (doorFrame == null)
        {
            Debug.LogWarning("Could not find DoorFrame_PlayerStart, skipping platform placement");
            return;
        }
        
        Vector3 doorPos = doorFrame.position;
        doorPos.y = 0f;
        
        List<Vector2Int> ps = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (grid[x, y] == Cell.PlayerStart)
                ps.Add(new Vector2Int(x, y));
        
        if (ps.Count == 0)
        {
            Debug.LogWarning("No PlayerStart cells found, skipping platform placement");
            return;
        }
        
        ps.Sort((a, b) =>
        {
            Vector3 wa = CellToWorld(a); wa.y = 0f;
            Vector3 wb = CellToWorld(b); wb.y = 0f;
            float da = (wa - doorPos).sqrMagnitude;
            float db = (wb - doorPos).sqrMagnitude;
            return da.CompareTo(db);
        });
        
        int take = Mathf.Min(8, ps.Count);
        Vector2Int bestA = ps[0], bestB = ps[0];
        float bestScore = float.PositiveInfinity;
        bool foundPair = false;
        
        for (int i = 0; i < take; i++)
        {
            for (int j = i + 1; j < take; j++)
            {
                Vector2Int a = ps[i];
                Vector2Int b = ps[j];
                
                if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) != 1)
                    continue;
                
                Vector3 wa = CellToWorld(a); wa.y = 0f;
                Vector3 wb = CellToWorld(b); wb.y = 0f;
                
                float score = (wa - doorPos).sqrMagnitude + (wb - doorPos).sqrMagnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestA = a;
                    bestB = b;
                    foundPair = true;
                }
            }
        }
        
        if (!foundPair)
        {
            Vector2Int a = ps[0];
            Vector2Int b = a;
            float bestN = float.PositiveInfinity;
            
            foreach (var dir in Dirs)
            {
                Vector2Int n = a + dir;
                if (!InBounds(n)) continue;
                if (grid[n.x, n.y] != Cell.PlayerStart) continue;
                
                Vector3 wn = CellToWorld(n); wn.y = 0f;
                float d = (wn - doorPos).sqrMagnitude;
                if (d < bestN)
                {
                    bestN = d;
                    b = n;
                }
            }
            
            bestA = a;
            bestB = b;
        }
        
        float floorYPos = 0f;
        
        CreateBox("Floor_PlayerStartPlatform_1", CellToWorld(bestA) + Vector3.up * floorYPos,
            new Vector3(cellSize, floorThickness, cellSize));
        CreateBox("Floor_PlayerStartPlatform_2", CellToWorld(bestB) + Vector3.up * floorYPos,
            new Vector3(cellSize, floorThickness, cellSize));
        
        Debug.Log($"Placed platforms on PlayerStart cells closest to door: {bestA} and {bestB}");
    }
    
    private void PlaceDeadEndDropBoxes()
    {
        if (dropBoxPrefab == null)
        {
            Debug.LogWarning("No drop box prefab assigned, skipping dead end drop box placement");
            return;
        }
        
        List<Vector2Int> deadEnds = FindDeadEnds();
        
        Vector2Int startCell = new Vector2Int((int)playerStartRect.x, (int)playerStartRect.y);
        
        List<Vector2Int> validDeadEnds = new List<Vector2Int>();
        foreach (var deadEnd in deadEnds)
        {
            float distance = Vector2Int.Distance(deadEnd, startCell);
            if (distance >= 5f)
            {
                validDeadEnds.Add(deadEnd);
            }
        }
        
        if (validDeadEnds.Count < 3)
        {
            Debug.LogWarning($"Only found {validDeadEnds.Count} valid dead ends (5+ cells from start), need at least 3 for drop boxes");
        }
        
        int dropBoxCount = Mathf.Min(3, validDeadEnds.Count);
        
        List<int> selectedIndices = new List<int>();
        for (int i = 0; i < dropBoxCount; i++)
        {
            int randomIndex;
            do
            {
                randomIndex = UnityEngine.Random.Range(0, validDeadEnds.Count);
            }
            while (selectedIndices.Contains(randomIndex));
            
            selectedIndices.Add(randomIndex);
            
            Vector2Int deadEndCell = validDeadEnds[randomIndex];
            
            Vector2Int openDirection = Vector2Int.zero;
            foreach (var dir in Dirs)
            {
                Vector2Int neighbor = deadEndCell + dir;
                if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] == Cell.Hall)
                {
                    openDirection = dir;
                    break;
                }
            }
            
            Vector2Int wallDirection = new Vector2Int(-openDirection.y, openDirection.x);
            
            Vector3 cellCenter = CellToWorld(deadEndCell);
            Vector3 wallOffset = new Vector3(wallDirection.x, 0, wallDirection.y) * (cellSize * 0.5f - 0.1f);
            Vector3 dropBoxPosition = cellCenter + wallOffset;
            dropBoxPosition.y = 2f;
            
            Quaternion dropBoxRotation = Quaternion.LookRotation(new Vector3(-wallDirection.x, 0, -wallDirection.y));
            
            string dropBoxCode = GenerateRoomCode();
            
            GameObject dropBox = Instantiate(dropBoxPrefab, generatedRoot);
            dropBox.name = "DropBox_" + dropBoxCode;
            dropBox.transform.position = dropBoxPosition;
            dropBox.transform.rotation = dropBoxRotation;
            
            Debug.Log($"Placed drop box {i + 1} on wall at dead end {deadEndCell} (distance from start: {Vector2Int.Distance(deadEndCell, startCell):F1}) with code {dropBoxCode}");
        }
        
        Debug.Log($"Placed {dropBoxCount} drop boxes at dead ends (filtered from {deadEnds.Count} total dead ends, {validDeadEnds.Count} valid)");
    }
    
    private List<Vector2Int> FindDeadEnds()
    {
        List<Vector2Int> deadEnds = new List<Vector2Int>();
        
        for (int x = margin; x < width - margin; x++)
        {
            for (int y = margin; y < height - margin; y++)
            {
                if (grid[x, y] == Cell.Hall)
                {
                    Vector2Int currentCell = new Vector2Int(x, y);
                    
                    int hallNeighbors = 0;
                    foreach (var dir in Dirs)
                    {
                        Vector2Int neighbor = currentCell + dir;
                        if (InBounds(neighbor) && grid[neighbor.x, neighbor.y] == Cell.Hall)
                        {
                            hallNeighbors++;
                        }
                    }
                    
                    if (hallNeighbors == 1)
                    {
                        deadEnds.Add(currentCell);
                    }
                }
            }
        }
        
        Debug.Log($"Found {deadEnds.Count} dead ends in the maze");
        return deadEnds;
    }
    
    private void PlaceRoomLights()
    {
        List<Vector3> placedLightPositions = new List<Vector3>();
        float minDistance = cellSize * 3f;
        
        List<Transform> hallwayRoofs = new List<Transform>();
        Dictionary<string, List<Transform>> roomRoofsByCode = new Dictionary<string, List<Transform>>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Roof")
            {
                hallwayRoofs.Add(child);
            }
            else if (child.name.StartsWith("Roof_"))
            {
                string code = child.name.Substring("Roof_".Length);
                
                if (!roomRoofsByCode.ContainsKey(code))
                {
                    roomRoofsByCode[code] = new List<Transform>();
                }
                roomRoofsByCode[code].Add(child);
            }
        }
        
        Debug.Log($"Found {hallwayRoofs.Count} hallway roofs and {roomRoofsByCode.Count} unique room codes");
        
        int hallwayLightCount = 0;
        int hallwayLightSkipped = 0;
        
        for (int i = 0; i < hallwayRoofs.Count; i++)
        {
            if ((i + 1) % 3 == 0)
            {
                Transform roof = hallwayRoofs[i];
                
                Vector2Int cellPos = WorldToCell(roof.position);
                if (IsInHardBlockZone(cellPos))
                {
                    hallwayLightSkipped++;
                    continue;
                }
                
                if (grid[cellPos.x, cellPos.y] == Cell.Wall)
                {
                    hallwayLightSkipped++;
                    continue;
                }
                
                Vector3 lightPosition = roof.position;
                lightPosition.y = roof.position.y - roof.localScale.y * 0.5f - 0.5f;
                
                bool tooClose = false;
                foreach (var existingPos in placedLightPositions)
                {
                    if (Vector3.Distance(lightPosition, existingPos) < minDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (tooClose)
                {
                    hallwayLightSkipped++;
                    continue;
                }
                
                string lightCode = GenerateRoomCode();
                GameObject light;
                
                if (lightPrefab != null)
                {
                    light = Instantiate(lightPrefab, roof);
                    light.name = "Light_" + lightCode;
                    light.transform.position = lightPosition;
                }
                else
                {
                    light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    light.name = "Light_" + lightCode;
                    light.transform.SetParent(roof);
                    light.transform.position = lightPosition;
                    light.transform.localScale = Vector3.one * 0.5f;
                    
                    Renderer renderer = light.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        renderer.sharedMaterial.color = Color.yellow;
                        renderer.sharedMaterial.SetFloat("_EmissiveIntensity", 2f);
                    }
                }
                
                placedLightPositions.Add(lightPosition);
                hallwayLightCount++;
            }
        }
        
        Debug.Log($"Placed {hallwayLightCount} hallway lights, skipped {hallwayLightSkipped} (3-cell spacing)");
        
        int roomLightCount = 0;
        int roomsWithFourLights = 0;
        
        foreach (var kvp in roomRoofsByCode)
        {
            string roomCode = kvp.Key;
            List<Transform> roofPieces = kvp.Value;
            
            Shuffle(roofPieces);
            
            int lightsPlacedInRoom = 0;
            
            List<Vector3> roomLightPositions = new List<Vector3>();
            float roomMinDistance = cellSize * 2f;
            
            foreach (Transform roofPiece in roofPieces)
            {
                if (lightsPlacedInRoom >= 4)
                    break;
                
                Vector2Int cellPos = WorldToCell(roofPiece.position);
                if (IsInHardBlockZone(cellPos))
                    continue;
                
                if (grid[cellPos.x, cellPos.y] == Cell.Wall)
                    continue;
                
                Vector3 lightPosition = roofPiece.position;
                lightPosition.y = roofPiece.position.y - roofPiece.localScale.y * 0.5f - 0.5f;
                
                bool tooCloseInRoom = false;
                foreach (var existingPos in roomLightPositions)
                {
                    if (Vector3.Distance(lightPosition, existingPos) < roomMinDistance)
                    {
                        tooCloseInRoom = true;
                        break;
                    }
                }
                
                if (tooCloseInRoom)
                    continue;
                
                GameObject light;
                
                if (lightPrefab != null)
                {
                    light = Instantiate(lightPrefab, roofPiece);
                    light.name = $"Light_{roomCode}_{lightsPlacedInRoom}";
                    light.transform.position = lightPosition;
                }
                else
                {
                    light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    light.name = $"Light_{roomCode}_{lightsPlacedInRoom}";
                    light.transform.SetParent(roofPiece);
                    light.transform.position = lightPosition;
                    light.transform.localScale = Vector3.one * 0.5f;
                    
                    Renderer renderer = light.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        renderer.sharedMaterial.color = Color.yellow;
                        renderer.sharedMaterial.SetFloat("_EmissiveIntensity", 2f);
                    }
                }
                
                roomLightPositions.Add(lightPosition);
                placedLightPositions.Add(lightPosition);
                roomLightCount++;
                lightsPlacedInRoom++;
            }
            
            if (lightsPlacedInRoom == 4)
            {
                roomsWithFourLights++;
            }
        }
        
        Debug.Log($"Placed {roomLightCount} room lights across {roomRoofsByCode.Count} rooms ({roomsWithFourLights} rooms have exactly 4 lights, 2-cell spacing)");
        Debug.Log($"Total lights placed: {hallwayLightCount + roomLightCount}");
        
        Transform playerStartDoor = null;
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name == "Door_PlayerStart")
            {
                playerStartDoor = child;
                break;
            }
        }
        
        if (playerStartDoor != null)
        {
            CreateCaveLight(playerStartDoor.position, "Light_PlayerStartDoor");
            Debug.Log("Created light at player start door");
        }
    }
    
    private void CreateCaveLight(Vector3 position, string lightName)
    {
        GameObject lightObj = new GameObject(lightName);
        lightObj.transform.SetParent(generatedRoot);
        lightObj.transform.position = position + Vector3.up * 5f;
        
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.6f);
        light.intensity = 15f;
        light.range = 25f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.8f;
    }
    
    private void FillDarkAreasWithBrokenLights()
    {
        List<Vector2Int> allLightCells = new List<Vector2Int>();
        
        foreach (Transform t in generatedRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("Light_"))
            {
                allLightCells.Add(WorldToCell(t.position));
            }
        }
        
        HashSet<Vector2Int> lightCellSet = new HashSet<Vector2Int>(allLightCells);
        
        Debug.Log($"Found {allLightCells.Count} working lights in grid");
        
        int brokenLightsPlaced = 0;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == Cell.Empty || grid[x, y] == Cell.Wall)
                    continue;
                
                Vector2Int currentCell = new Vector2Int(x, y);
                
                if (IsInHardBlockZone(currentCell))
                    continue;
                
                if (lightCellSet.Contains(currentCell))
                    continue;
                
                float minDistanceToLight = float.MaxValue;
                foreach (var lightCell in allLightCells)
                {
                    float distance = Vector2Int.Distance(currentCell, lightCell);
                    if (distance < minDistanceToLight)
                    {
                        minDistanceToLight = distance;
                    }
                }
                
                if (minDistanceToLight > 3f)
                {
                    Vector3 cellWorldPos = CellToWorld(currentCell);
                    
                    float roofYPos = wallHeight + roofThickness / 2;
                    if (grid[x, y] == Cell.PlayerStart)
                    {
                        roofYPos = playerStartRoomHeight + roofThickness / 2;
                    }
                    else if (grid[x, y] == Cell.Room)
                    {
                        roofYPos = (wallHeight * 2.0f) + roofThickness / 2;
                    }
                    
                    Vector3 brokenLightPosition = cellWorldPos + Vector3.up * roofYPos;
                    Vector3 brokenLightScale = new Vector3(cellSize, roofThickness, cellSize);
                    
                    foreach (Transform child in generatedRoot)
                    {
                        if ((child.name == "Roof" || child.name.StartsWith("Roof_")) &&
                            Vector3.Distance(child.position, brokenLightPosition) < 0.1f)
                        {
                            DestroyImmediate(child.gameObject);
                            break;
                        }
                    }
                    
                    GameObject brokenLight;
                    
                    if (brokenLightPrefab != null)
                    {
                        brokenLight = Instantiate(brokenLightPrefab, generatedRoot);
                        brokenLight.name = "BrokenLight";
                        brokenLight.transform.position = brokenLightPosition;
                        brokenLight.transform.localScale = brokenLightScale;
                    }
                    else
                    {
                        brokenLight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        brokenLight.name = "BrokenLight";
                        brokenLight.transform.SetParent(generatedRoot);
                        brokenLight.transform.position = brokenLightPosition;
                        brokenLight.transform.localScale = brokenLightScale;
                        
                        Renderer renderer = brokenLight.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            renderer.sharedMaterial.color = new Color(0.3f, 0.3f, 0.3f);
                        }
                    }
                    
                    allLightCells.Add(currentCell);
                    brokenLightsPlaced++;
                }
            }
        }
        
        Debug.Log($"Filled dark areas with {brokenLightsPlaced} broken lights (>3 cells from any light)");
    }
    
    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.x / cellSize + width / 2f);
        int y = Mathf.RoundToInt(worldPos.z / cellSize + height / 2f);
        return new Vector2Int(x, y);
    }
    
    private void PlaceDoorAtLocation(Vector3 position, Vector3 openingScale, string roomCode, bool isStorageRoom, bool isMainDoor, string side = "")
    {
        bool isVertical = openingScale.x < openingScale.z;
        Quaternion doorRotation;
        
        if (!string.IsNullOrEmpty(side))
        {
            if (side == "North")
                doorRotation = Quaternion.Euler(0, 180, 0);
            else if (side == "South")
                doorRotation = Quaternion.Euler(0, 0, 0);
            else if (side == "East")
                doorRotation = Quaternion.Euler(0, -90, 0);
            else if (side == "West")
                doorRotation = Quaternion.Euler(0, 90, 0);
            else
                doorRotation = isVertical ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
        }
        else
        {
            doorRotation = isVertical ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
        }
        
        float doorFrameHeight = 4f;
        float doorFrameWidth = isVertical ? openingScale.z : openingScale.x;
        float doorFrameThickness = isVertical ? openingScale.x : openingScale.z;
        
        Vector3 doorFramePosition = new Vector3(position.x, 2f, position.z);
        
        GameObject doorFramePrefabToUse = isMainDoor && mainDoorFramePrefab != null ? mainDoorFramePrefab : 
                                          isMainDoor && playerStartDoorFramePrefab != null ? playerStartDoorFramePrefab :
                                          doorFramePrefab;
        
        if (doorFramePrefabToUse != null)
        {
            GameObject doorFrame = Instantiate(doorFramePrefabToUse, generatedRoot);
            doorFrame.name = "DoorFrame_" + roomCode;
            doorFrame.transform.position = doorFramePosition;
            doorFrame.transform.rotation = doorRotation;
            
            if (isMainDoor && playerStartDoorFramePrefab != null)
            {
                Renderer renderer = doorFrame.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds = renderer.bounds;
                    Debug.Log($"Using player start door frame prefab with bounds size: {bounds.size}");
                }
            }
            else
            {
                doorFrame.transform.localScale = new Vector3(doorFrameWidth, doorFrameHeight, doorFrameThickness);
            }
        }
        else
        {
            GameObject doorFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorFrame.name = "DoorFrame_" + roomCode;
            doorFrame.transform.SetParent(generatedRoot);
            doorFrame.transform.position = doorFramePosition;
            doorFrame.transform.rotation = doorRotation;
            doorFrame.transform.localScale = new Vector3(doorFrameWidth, doorFrameHeight, doorFrameThickness);
            
            Renderer renderer = doorFrame.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                renderer.sharedMaterial.color = new Color(0.4f, 0.3f, 0.2f);
            }
        }
        
        float doorWidth = 2f;
        float doorHeight = 3.2f;
        float doorYPosition = isMainDoor ? 1.8f : 1.6f;
        Vector3 doorPosition = new Vector3(position.x, doorYPosition, position.z);
        
        GameObject doorPrefabToUse = isMainDoor && mainDoorPrefab != null ? mainDoorPrefab :
                                     isMainDoor && playerStartDoorPrefab != null ? playerStartDoorPrefab :
                                     doorPrefab;
        
        if (doorPrefabToUse != null)
        {
            GameObject door = Instantiate(doorPrefabToUse, generatedRoot);
            door.name = "Door_" + roomCode;
            door.transform.position = doorPosition;
            door.transform.rotation = doorRotation;
            
            if (isMainDoor && playerStartDoorPrefab != null)
            {
                Renderer renderer = door.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds = renderer.bounds;
                    Debug.Log($"Using player start door prefab with bounds size: {bounds.size}");
                }
            }
            else
            {
                if (isStorageRoom)
                {
                    door.transform.localScale = new Vector3(doorWidth, doorHeight, doorFrameThickness);
                }
                else
                {
                    door.transform.localScale = new Vector3(4f, doorHeight, doorFrameThickness);
                }
            }
        }
        else
        {
            GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Door_" + roomCode;
            door.transform.SetParent(generatedRoot);
            door.transform.position = doorPosition;
            door.transform.rotation = doorRotation;
            
            if (isStorageRoom)
            {
                door.transform.localScale = new Vector3(doorWidth, doorHeight, doorFrameThickness);
            }
            else
            {
                door.transform.localScale = new Vector3(4f, doorHeight, doorFrameThickness);
            }
            
            Renderer renderer = door.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                renderer.sharedMaterial.color = new Color(0.6f, 0.4f, 0.2f);
            }
        }
    }

    
    private void EnforceDoorLimitsPostGeneration()
    {
        Dictionary<string, List<GameObject>> roomUpperWalls = new Dictionary<string, List<GameObject>>();
        
        foreach (Transform child in generatedRoot)
        {
            if (child.name.StartsWith("UpperWall_"))
            {
                string roomCode = child.name.Substring("UpperWall_".Length);
                
                if (!roomUpperWalls.ContainsKey(roomCode))
                    roomUpperWalls[roomCode] = new List<GameObject>();
                
                roomUpperWalls[roomCode].Add(child.gameObject);
            }
        }
        
        foreach (var kvp in roomUpperWalls)
        {
            string roomCode = kvp.Key;
            List<GameObject> upperWalls = kvp.Value;
            
            int doorwayCount = upperWalls.Count;
            int maxDoors = roomCode == "PlayerStart" ? 1 : maxRoomDoors;
            
            Debug.Log($"Room [{roomCode}]: {doorwayCount} UpperWalls (doorways) detected, max allowed: {maxDoors}");
            
            if (doorwayCount > maxDoors)
            {
                int excessCount = doorwayCount - maxDoors;
                List<GameObject> toFill = SelectDoorwaysToFill(upperWalls, excessCount);
                
                foreach (GameObject upperWall in toFill)
                {
                    GameObject lowerWall = Instantiate(upperWall, generatedRoot);
                    lowerWall.name = $"LowerWall_{roomCode}";
                    
                    Vector3 currentPos = upperWall.transform.position;
                    lowerWall.transform.position = new Vector3(currentPos.x, 2f, currentPos.z);
                    
                    upperWall.name = $"FilledDoorway_{roomCode}";
                    
                    Debug.Log($"  Filled doorway for room [{roomCode}] at position {currentPos}");
                }
                
                Debug.Log($"Room [{roomCode}]: Filled {excessCount} excess doorways");
            }
        }
    }
    
    private List<GameObject> SelectDoorwaysToFill(List<GameObject> doorways, int countToFill)
    {
        List<GameObject> toFill = new List<GameObject>();
        List<GameObject> remaining = new List<GameObject>(doorways);
        
        while (toFill.Count < countToFill && remaining.Count > 0)
        {
            GameObject bestCandidate = null;
            int maxAdjacentCount = -1;
            
            foreach (GameObject doorway in remaining)
            {
                int adjacentCount = CountAdjacentDoorways(doorway, remaining);
                
                if (adjacentCount > maxAdjacentCount)
                {
                    maxAdjacentCount = adjacentCount;
                    bestCandidate = doorway;
                }
                else if (adjacentCount == maxAdjacentCount && bestCandidate != null)
                {
                    float currentSize = doorway.transform.localScale.x * doorway.transform.localScale.z;
                    float bestSize = bestCandidate.transform.localScale.x * bestCandidate.transform.localScale.z;
                    if (currentSize < bestSize)
                    {
                        bestCandidate = doorway;
                    }
                }
            }
            
            if (bestCandidate != null)
            {
                toFill.Add(bestCandidate);
                remaining.Remove(bestCandidate);
            }
            else
            {
                break;
            }
        }
        
        return toFill;
    }
    
    private int CountAdjacentDoorways(GameObject doorway, List<GameObject> allDoorways)
    {
        int count = 0;
        Vector3 pos = doorway.transform.position;
        float checkDistance = cellSize * 1.5f;
        
        foreach (GameObject other in allDoorways)
        {
            if (other == doorway) continue;
            
            Vector3 otherPos = other.transform.position;
            float distance = Vector3.Distance(
                new Vector3(pos.x, 0, pos.z), 
                new Vector3(otherPos.x, 0, otherPos.z)
            );
            
            if (distance < checkDistance)
            {
                count++;
            }
        }
        
        return count;
    }
    
    private void SeparateTouchingRooms()
    {
        int wallsAdded = 0;
        
        for (int i = 0; i < roomRects.Count; i++)
        {
            for (int j = i + 1; j < roomRects.Count; j++)
            {
                RectInt room1 = roomRects[i];
                RectInt room2 = roomRects[j];
                string code1 = roomCodes.ContainsKey(room1) ? roomCodes[room1] : "UNKNOWN";
                string code2 = roomCodes.ContainsKey(room2) ? roomCodes[room2] : "UNKNOWN";
                
                List<Vector2Int> touchingCells = FindTouchingCells(room1, room2);
                
                if (touchingCells.Count > 0)
                {
                    Debug.Log($"Room [{code1}] touches Room [{code2}] at {touchingCells.Count} cells");
                    
                    foreach (var cell in touchingCells)
                    {
                        Vector3 worldPos = CellToWorld(cell);
                        
                        for (int d = 0; d < 4; d++)
                        {
                            Vector2Int neighbor = cell + Dirs[d];
                            if (!InBounds(neighbor)) continue;
                            
                            bool neighborInRoom1 = room1.Contains(neighbor);
                            bool neighborInRoom2 = room2.Contains(neighbor);
                            
                            if ((room1.Contains(cell) && neighborInRoom2) || (room2.Contains(cell) && neighborInRoom1))
                            {
                                float wallHeightToUse = wallHeight * 2.0f;
                                
                                Vector3 wallPos = worldPos + new Vector3(Dirs[d].x * cellSize / 2f, wallHeightToUse / 2f, Dirs[d].y * cellSize / 2f);
                                Vector3 wallSize = (Dirs[d].x != 0) ? new Vector3(wallThickness, wallHeightToUse, cellSize) : new Vector3(cellSize, wallHeightToUse, wallThickness);
                                
                                string roomCode = room1.Contains(cell) ? code1 : code2;
                                string wallName = $"Wall_{roomCode}_RoomSeparator";
                                
                                CreateBox(wallName, wallPos, wallSize);
                                wallsAdded++;
                            }
                        }
                    }
                }
            }
        }
        
        if (wallsAdded > 0)
        {
            Debug.Log($"Added {wallsAdded} walls to separate touching rooms");
        }
    }
    
    private List<Vector2Int> FindTouchingCells(RectInt room1, RectInt room2)
    {
        List<Vector2Int> touchingCells = new List<Vector2Int>();
        
        for (int x = room1.xMin; x < room1.xMax; x++)
        {
            for (int y = room1.yMin; y < room1.yMax; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                
                foreach (var dir in Dirs)
                {
                    Vector2Int neighbor = cell + dir;
                    if (room2.Contains(neighbor))
                    {
                        if (!touchingCells.Contains(cell))
                            touchingCells.Add(cell);
                        break;
                    }
                }
            }
        }
        
        return touchingCells;
    }
}
