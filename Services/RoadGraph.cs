using Dapper;
using Nusantara.Database;
using Nusantara.Models;

namespace Nusantara.Services;

// Graph jaringan jalan berbasis node (GDD bagian 9) + Dijkstra
public class RoadGraph
{
    static RoadGraph? inst;
    public static RoadGraph I => inst ??= new RoadGraph();

    public Dictionary<int, Node> Nodes;
    public Dictionary<int, Edge> EdgeById;
    readonly Dictionary<int, List<Edge>> adj = new();

    RoadGraph()
    {
        using var c = Db.Open();
        Nodes = c.Query<Node>("SELECT * FROM nodes").ToDictionary(n => n.Id);
        EdgeById = c.Query<Edge>("SELECT * FROM edges").ToDictionary(e => e.Id);
        foreach (var id in Nodes.Keys) adj[id] = new List<Edge>();
        foreach (var e in EdgeById.Values) { adj[e.A].Add(e); adj[e.B].Add(e); }
    }

    public int Id(string name) => Nodes.Values.First(n => n.Name == name).Id;

    public List<Edge>? Path(int s, int t, bool allowFerry, double tollMult)
    {
        var dist = new Dictionary<int, double> { [s] = 0 };
        var prev = new Dictionary<int, (int node, Edge edge)>();
        var pq = new PriorityQueue<int, double>();
        pq.Enqueue(s, 0);
        while (pq.TryDequeue(out var u, out var d))
        {
            if (d > dist[u]) continue;
            if (u == t) break;
            foreach (var e in adj[u])
            {
                if (e.Kind == "FERRY" && !allowFerry) continue;
                int v = e.A == u ? e.B : e.A;
                double nd = d + e.Km * (e.Toll ? tollMult : 1.0);
                if (!dist.TryGetValue(v, out var od) || nd < od) { dist[v] = nd; prev[v] = (u, e); pq.Enqueue(v, nd); }
            }
        }
        if (!dist.ContainsKey(t)) return null;
        var list = new List<Edge>();
        int cur = t;
        while (cur != s) { var (p, e) = prev[cur]; list.Add(e); cur = p; }
        list.Reverse();
        return list;
    }
}