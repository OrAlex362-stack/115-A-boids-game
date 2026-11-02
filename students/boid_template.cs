using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace BoidArena
{
    // ============================================================================
    // 學生範本：複製這個檔，改檔名成 boid_{你的名字}.cs，
    // 並把 class 名稱改成 Boid_{你的名字}、Name 改成你的隊名（只改這個檔，不要動 core、host）
    //
    // 這個檔只列出「你拿得到什麼」，怎麼用、要寫什麼行為，全部由你自己決定。
    // 所有資料都是唯讀的複本，改它不會影響別人，也不會影響比賽。
    // ============================================================================
    //
    // 【每隻鳥每一步，引擎呼叫一次 Decide，傳進 4 組資料】
    //
    // ■ self（SelfInfo）：這一隻自己
    //     self.Position          Point3d    我的位置
    //     self.Velocity          Vector3d   我的速度（方向＝我面向哪裡）
    //     self.Team              int        我的隊伍編號
    //     self.Name              string     我的隊名（等於下面 Name 屬性的值）
    //     self.InvincibleSteps   int        我還剩幾步無敵（0 = 沒有）
    //     self.Stars             List<Point3d>  視野內的五芒星位置（沒有就是空清單）
    //     self.Rng               Random     我的專屬亂數產生器（要亂數只能用它）
    //
    // ■ neighbors（List<Neighbor>）：視野內的其他鳥（不含自己；沒有就是空清單）。每一個 Neighbor 有：
    //     Position           Point3d    它的位置
    //     Velocity           Vector3d   它的速度
    //     Team               int        它的隊伍編號
    //     Name               string     它所屬隊伍的隊名
    //     Relation           Relation   對我來說它是什麼身份：Ally 友軍／Enemy 敵軍／Neutral 不相關
    //                                   （Self 這個值是留給 table.Get 查自己用的）
    //                                   預設：同隊＝友軍，異隊＝敵軍；可以在 Declare 裡改
    //     IsFriend           bool       是不是同隊（不受 Declare 影響）
    //     InvincibleSteps    int        它還剩幾步無敵（0 = 沒有）
    //     Distance           double     它離我多遠
    //
    // ■ foods（List<Point3d>）：視野內的食物位置（沒有就是空清單）
    //
    // ■ world（World）：場地與物理限制，全隊相同
    //     world.Center       Point3d    場地中心（原點）
    //     world.HalfSize     double     場地目前的半邊長（正方形；縮小期會變小）
    //     world.Step         int        現在是第幾步
    //     world.Phase        int        現在的階段：1 穩定期、2 縮小期、3 決戰期
    //     world.ViewRadius   double     視野半徑（10）
    //     world.MaxSpeed     double     速度上限（0.5）
    //     world.MaxForce     double     轉向力上限（0.05）
    //     world.Obstacles    IList<Obstacle>  目前場上的障礙物（矩形，與座標軸平行）。每個 Obstacle 有：
    //         Center         Point3d    中心
    //         HalfWidth      double     X 方向的半寬
    //         HalfHeight     double     Y 方向的半高
    //         ClosestPoint(Point3d p)   Point3d  矩形上離 p 最近的點
    //         DistanceTo(Point3d p)     double   p 到矩形邊緣的距離（在裡面是 0）
    //
    // 【可以呼叫的函式】
    //     Steering.Unit(Vector3d v)                          回傳 v 的單位向量（太短就回零向量）
    //     Steering.Seek(Vector3d 想去的方向, Vector3d 目前速度, World world)
    //                                                        回傳「往那個方向轉」的轉向力（長度已限制在 MaxForce）
    //
    // 【開賽前只呼叫一次的 Declare(RelationTable table)，table 有：】
    //     table.MyName        string          我的隊名
    //     table.AllNames      IList<string>   這一場所有參賽隊伍的名字（含自己）
    //     table.Ally(string 隊名)               把某隊標成友軍
    //     table.Enemy(string 隊名)              把某隊標成敵軍
    //     table.Neutral(string 隊名)            把某隊標成不相關
    //     table.Get(string 隊名)  Relation       查某隊現在的身份（自己這隊回傳 Self）
    //   不需要的話，可以把 `: IBrain, IDeclareRelations` 改成 `: IBrain`，並刪掉 Declare。
    //
    // 【回傳值】
    //     Decide 回傳一個 Vector3d，代表「我想往哪轉」。引擎會自動把它限制在 MaxForce 以內，
    //     而且這是 2D 遊戲：Z 方向一律被丟掉。
    //
    // 【遊戲規則裡和你有關的事實】
    //     - 全場 2D，場地是以原點為中心的正方形；一回合約 3 分鐘，存活最多的隊伍獲勝
    //     - 吃到食物：該隻複製成兩隻（全場上限 500 隻，滿了不能出生）
    //     - 吃到五芒星：無敵 100 步，殺不死，但照樣能殺別人
    //     - 異隊碰到時：A 的前方 60 度碰到 B 的正後方 60 度，A 殺死 B；正面對撞或側邊碰撞互相彈開
    //     - 身份（友軍／敵軍／不相關）只是你自己用的標籤，不改變擊殺規則
    //     - 每隊每步所有 Decide 加起來要在 15 毫秒內；丟例外、回傳 NaN 或太慢，整隊淘汰
    //     - 顏色由老師分配，你不用寫
    //
    // 【禁止】（違反的檔案不會參賽）
    //     不可使用檔案、網路、執行緒、反射；不可自己 new Random、不可用 DateTime.Now、Guid.NewGuid、Environment；
    //     不可有 static 可變欄位（static readonly 可以）；不可碰 Arena、Agent、Roster；
    //     不要用 Vector3d.Unitize（改用 Steering.Unit）；需要額外的輔助類別，名稱前面加上你的名字
    // ============================================================================
    public class Boid_template : IBrain, IDeclareRelations
    {
        public string Name { get { return "template"; } }

        // 開賽前引擎只呼叫一次
        public void Declare(RelationTable table)
        {
        }

        // 每隻鳥每一步都會呼叫一次，回傳「想往哪轉」的向量
        public Vector3d Decide(SelfInfo self, List<Neighbor> neighbors, List<Point3d> foods, World world)
        {
            return Vector3d.Zero;
        }
    }
}
