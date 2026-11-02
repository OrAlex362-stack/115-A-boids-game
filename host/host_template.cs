//@LOADS@
// Boids 競技場：把整份貼進 GH 的 C# Script 元件，按編輯器的綠色執行鈕。
// 學生檔增減時：重跑 python tools\gen_host.py [--check]，只需要更新上面的 #load 清單（引擎、畫面、播放都在 host\GameHost.cs）
// 輸入（seed、minutes、starRate 改了要按 reset 才生效；不接就用預設）
//   reset bool 重來（Button）　run bool 自動前進／暫停（Boolean Toggle）
//   seed 亂數種子（0 或不接 = Arena.GlobalSeed）　minutes 一回合分鐘數（預設 3）　starRate 五芒星每步機率（預設 Arena.StarRate）
// 輸出：只有 info；畫面全部畫在視埠
using Grasshopper.Kernel;
using Rhino.Geometry;
using BoidArena;

public class Script_Instance : GH_ScriptInstance
{
    private GameHost game = new GameHost();

    private void RunScript(bool reset, bool run, object seed, double minutes, object starRate,
        ref object points, ref object colors, ref object foods, ref object box, ref object info)
    {
        game.Setup(reset, seed, minutes, starRate, typeof(Script_Instance).Assembly);   // 建立（或重來）一場比賽
        info = game.Run(run, Component);                                                // 往前跑
        points = Point3d.Origin;   // 輸出全是 null 時 GH 不會畫預覽，給一個原點讓預覽開啟
    }

    public override BoundingBox ClippingBox { get { return game.Box; } }
    public override void DrawViewportWires(IGH_PreviewArgs args) { game.Draw(args.Display); }
}
