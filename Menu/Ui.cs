namespace Nusantara.Menus;

// Fungsi bantu tampilan & input yang dipakai semua menu
public static class Ui
{
    public static string Ask(string q)
    {
        Console.Write(q);
        return (Console.ReadLine() ?? "").Trim();
    }

    // Kosong = nilai default. Teks bukan angka = diminta ulang (bukan diam-diam jadi default).
    public static int Num(string q, int def = -1)
    {
        while (true)
        {
            var s = Ask(q);
            if (s == "") return def;
            if (int.TryParse(s, out var v)) return v;
            Console.WriteLine("  Masukkan angka saja.");
        }
    }

    public static bool Yes(string q) => Ask(q).ToLower() == "y";

    public static void Line(char c = '-', int n = 62) => Console.WriteLine(new string(c, n));

    public static void Title(string t)
    {
        Console.WriteLine();
        Line('=');
        Console.WriteLine("  " + t.ToUpper());
        Line('=');
    }

    public static void Section(string t)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {t} --");
    }

    public static void Empty(string msg) => Console.WriteLine("  (" + msg + ")");

    public static void Pause() => Ask("\nTekan Enter untuk lanjut...");

    // Daftar pilihan: satu per baris, ditutup dengan angka 0
    public static void Options(string[] items, string zero = "Kembali")
    {
        Console.WriteLine();
        for (int i = 0; i < items.Length; i++)
            Console.WriteLine($"  {i + 1,2}. {items[i]}");
        Console.WriteLine($"   0. {zero}");
    }

    // Pertanyaan bergaya pilihan angka. Kosong = default; di luar daftar = diminta ulang.
    public static int Choose(string title, string[] opts, int def = 1)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        for (int i = 0; i < opts.Length; i++)
            Console.WriteLine($"  {i + 1}. {opts[i]}");
        while (true)
        {
            var s = Ask($"Pilih [{def}]: ");
            if (s == "") return def;
            if (int.TryParse(s, out var v) && v >= 1 && v <= opts.Length) return v;
            Console.WriteLine($"  Pilihan harus angka 1-{opts.Length}.");
        }
    }

    public static string Bar(int pct)
    {
        int f = Math.Clamp(pct / 10, 0, 10);
        return $"[{new string('#', f)}{new string('.', 10 - f)}] {pct,3}%" + (pct < 15 ? "  !! KRITIS" : "");
    }
}