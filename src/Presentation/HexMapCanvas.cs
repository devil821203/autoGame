using Cultivation.Domain;
using Godot;

namespace Cultivation.Presentation;

public partial class HexMapCanvas : Control
{
    public event Action<HexCoord>? Selected;
    private WorldState? world;
    private HexCoord? selected;
    private float zoom = 1;
    private Vector2 pan = new(55, 45);
    private bool dragging;
    private const float Radius = 26;
    public bool FollowPlayer { get; set; } = true;
    public Vector2 PlayerScreenPosition => world?.HexMap is { } map ? Center(HexNavigation.Position(map,
        world.Characters.Single(c => c.Id == world.ControlledCharacterId))) : Vector2.Zero;
    private void Follow()
    {
        if (!FollowPlayer || world?.HexMap is not { } map) return;
        var point = HexNavigation.Position(map, world.Characters.Single(c => c.Id == world.ControlledCharacterId));
        pan += Size / 2 - Center(point);
    }
    public override void _Ready() { CustomMinimumSize = new(850, 430); ClipContents = true; MouseFilter = MouseFilterEnum.Stop; }
    private Vector2 Center(HexCoord c) => new Vector2(Mathf.Sqrt(3) * Radius * (c.Q + c.R / 2f), Radius * 1.5f * c.R) * zoom + pan;
    private Vector2[] Polygon(HexCoord c) => Enumerable.Range(0, 6).Select(i => Center(c) + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * Radius * zoom).ToArray();
    public void Refresh(WorldState snapshot, HexCoord? selection) { world = snapshot; selected = selection; Follow(); QueueRedraw(); }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) { dragging = button.Pressed; AcceptEvent(); }
            if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float next = Mathf.Clamp(zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), .6f, 2.2f);
                pan = button.Position - (button.Position - pan) * next / zoom; zoom = next; Follow(); QueueRedraw(); AcceptEvent();
            }
            if (button.Pressed && button.ButtonIndex == MouseButton.Left && world?.HexMap is { } map)
            {
                var tile = map.Tiles.FirstOrDefault(t => Geometry2D.IsPointInPolygon(button.Position, Polygon(t.Coord)));
                if (tile is not null) Selected?.Invoke(tile.Coord); AcceptEvent();
            }
        }
        if (input is InputEventMouseMotion motion && dragging && !FollowPlayer) { pan += motion.Relative; QueueRedraw(); AcceptEvent(); }
    }
    public override void _Draw()
    {
        Follow();
        DrawRect(new(Vector2.Zero, Size), new Color("182625"));
        if (world?.HexMap is not { } map) { Text(new(20, 40), "請建立「六角世界／領地測試」情境", Colors.White); return; }
        foreach (var tile in map.Tiles)
        {
            var p = Center(tile.Coord); if (p.X < -60 || p.Y < -60 || p.X > Size.X + 60 || p.Y > Size.Y + 60) continue;
            var polygon = Polygon(tile.Coord);
            var color = tile.Mountain ? new Color("747e83") : tile.Terrain == HexTerrain.Lake ? new Color("407db2") :
                tile.Terrain == HexTerrain.Desert ? new Color("c1a86b") : tile.Forest ? new Color("315e43") : new Color("809a59");
            DrawColoredPolygon(polygon, color);
            DrawPolyline(polygon.Append(polygon[0]).ToArray(), new Color("293e37"), 1, true);
            if (tile.LocalOrganizationId is not null)
            {
                var border = tile.LocalOrganizationId switch { "sect" => Colors.Cyan, "gang" => Colors.Orange, _ => Colors.IndianRed };
                DrawPolyline(polygon.Select(v => p + (v - p) * .83f).Append(p + (polygon[0] - p) * .83f).ToArray(), border, 2, true);
            }
            string icon = tile.Mountain ? "山" : tile.Terrain == HexTerrain.Lake ? "水" : tile.Forest ? "林" : "";
            if (tile.Settlement is not null) icon = tile.PlaceId == "sect" ? "宗" : tile.PlaceId == "town" ? "鎮" : "村";
            if (map.Sites.Any(s => s.Anchor == tile.Coord && s.Kind == SiteKind.City)) icon = "城";
            if (tile.PlaceId is "grove" or "alchemy" or "dungeon") icon = tile.PlaceId switch { "grove" => "藥", "alchemy" => "丹", _ => "險" };
            if (icon.Length > 0) Text(p + new Vector2(-8, 5), icon, Colors.White);
            foreach (var b in map.Buildings.Where(b => b.Coord == tile.Coord && b.Status != BuildingStatus.Cancelled))
                DrawRect(new(p + new Vector2(7, -17) * zoom, new Vector2(9, 9) * zoom), b.Status == BuildingStatus.Complete ? Colors.White : Colors.Yellow);
            if (selected == tile.Coord) DrawPolyline(polygon.Append(polygon[0]).ToArray(), Colors.Gold, 3, true);
        }
        var player = world.Characters.Single(c => c.Id == world.ControlledCharacterId);
        var position = HexNavigation.Position(map, player);
        var trip = map.Journeys.SingleOrDefault(t => t.ActorId == player.Id);
        if (trip is not null)
        {
            var points = new[] { position }.Concat(trip.Remaining).Select(Center).ToArray();
            if (points.Length > 1) DrawPolyline(points, Colors.Gold, 3, true);
        }
        DrawCircle(Center(position) + new Vector2(-10, -9), 6 * zoom, Colors.Gold);
    }
    private void Text(Vector2 position, string text, Color color) => DrawString(ThemeDB.FallbackFont, position, text, fontSize: 16, modulate: color);
}
