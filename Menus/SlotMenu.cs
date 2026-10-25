using Nusantara.Services;

namespace Nusantara.Menus;

// Layar awal: pilih slot simpan, buat perusahaan baru, hapus, atau pulihkan dari cadangan.
public static class SlotMenu
{
    static string Legal(int tier) => tier == 1 ? "Perorangan" : tier == 2 ? "CV" : "PT";

    static void Show(List<SlotInfo> slots)
    {
        Ui.Title("Nusantara Logistics Manager - Slot Simpan");
        foreach (var s in slots)
        {
            if (!s.Exists) { Console.WriteLine($"  {s.No,2}. [kosong]"); continue; }
            if (s.Broken)
            {
                Console.WriteLine($"  {s.No,2}. [RUSAK] {s.Problem}" + (s.HasBackup ? "  (ada cadangan, tekan R)" : ""));
                continue;
            }
            Console.WriteLine($"  {s.No,2}. {s.Company} ({Legal(s.Tier)}){(s.Bankrupt ? "  [BANGKRUT]" : "")}");
            Console.WriteLine($"      {s.When} | {s.Trucks} truk | {Rules.Rp(s.Cash)} | disimpan {s.SavedLocal}");
        }
        Console.WriteLine();
        Console.WriteLine("  Ketik nomor slot untuk lanjut / membuat perusahaan baru.");
        Console.WriteLine("  D = hapus slot   R = pulihkan dari cadangan   0 = keluar");
    }

    static int AskSlot(string q)
    {
        var s = Ui.Ask(q);
        return int.TryParse(s, out var n) && n >= 1 && n <= SaveSlots.MaxSlots ? n : -1;
    }

    // Mengembalikan id perusahaan pemain, atau -1 kalau pemain memilih keluar.
    public static int Run()
    {
        while (true)
        {
            var slots = SaveSlots.List();
            Show(slots);
            var a = Ui.Ask("Pilih: ").ToLowerInvariant();

            if (a == "0") return -1;

            if (a == "d")
            {
                int n = AskSlot("Hapus slot nomor: ");
                if (n < 0) { Console.WriteLine("  Nomor slot tidak valid."); continue; }
                if (!slots[n - 1].Exists) { Console.WriteLine("  Slot itu sudah kosong."); continue; }
                if (Ui.Ask("Ketik HAPUS untuk memastikan (data & cadangan hilang permanen): ") != "HAPUS")
                { Console.WriteLine("  Dibatalkan."); continue; }
                Console.WriteLine("  " + SaveSlots.Delete(n));
                continue;
            }

            if (a == "r")
            {
                int n = AskSlot("Pulihkan slot nomor: ");
                if (n < 0) { Console.WriteLine("  Nomor slot tidak valid."); continue; }
                if (!Ui.Yes("Isi slot akan diganti dengan cadangan terakhir. Lanjut? (y/n): ")) { Console.WriteLine("  Dibatalkan."); continue; }
                Console.WriteLine("  " + SaveSlots.RestoreBackup(n));
                continue;
            }

            if (!int.TryParse(a, out var no) || no < 1 || no > SaveSlots.MaxSlots)
            {
                Console.WriteLine("  Pilihan tidak valid.");
                continue;
            }

            var slot = slots[no - 1];
            try
            {
                if (slot.Exists)
                {
                    if (slot.Broken)
                    {
                        Console.WriteLine("  Slot ini rusak" + (slot.HasBackup ? ". Gunakan R untuk memulihkan dari cadangan." : " dan tidak punya cadangan."));
                        continue;
                    }
                    return SaveSlots.Open(no);
                }
                int id = NewCompany(no);
                if (id == -2) continue;   // dibatalkan
                return id;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Gagal membuka slot: " + ex.Message);
                if (slot.Exists && slot.HasBackup) Console.WriteLine("  Coba pulihkan dari cadangan dengan menekan R.");
            }
        }
    }

    // -2 = pemain membatalkan
    static int NewCompany(int no)
    {
        while (true)
        {
            var raw = Ui.Ask("Nama perusahaan (kosongkan untuk batal): ");
            if (raw.Trim() == "") return -2;
            var err = SaveSlots.ValidateName(raw, out _);
            if (err == null) return SaveSlots.Create(no, raw);
            Console.WriteLine("  " + err);
        }
    }
}