"""產生 host\\host_script.cs：host_template.cs 加上 core 與 students 底下每個 .cs 的 #load 絕對路徑。

用法
  python tools\\gen_host.py             全部載入（學生自己測試）
  python tools\\gen_host.py --check     老師合併：每個學生檔先檢查，有問題的排除，不讓一個人卡住全班
  python tools\\gen_host.py --examples  另外載入 examples
  python tools\\gen_host.py --students 資料夾   改用別的學生資料夾

--check 三步（需要 .NET SDK）
  1. 掃描禁用寫法（檔案、網路、執行緒、反射、自己 new Random、static 可變欄位、碰引擎類別）
  2. 單獨編譯並試跑 300 步：編譯錯誤、例外、NaN、太慢、無窮迴圈（逾時）都排除
  3. 通過的檔合在一起再編譯試跑：同名 class 衝突、合併後才出錯的隊、超過 10 隊上限的檔，逐一找出並排除
結果依檔案內容快取在 tools\\.check_cache.json
"""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "host" / "host_template.cs"
OUT = ROOT / "host" / "host_script.cs"
CHECK = ROOT / "tools" / "check"
CACHE = ROOT / "tools" / ".check_cache.json"
TIMEOUT = 60  # 試跑秒數上限；超過視為無窮迴圈

# 禁用寫法：(正規表示式, 原因)
FORBIDDEN = [
    (r"\bSystem\.IO\b|\bFile\.|\bDirectory\.", "檔案存取"),
    (r"\bSystem\.Net\b|\bHttpClient\b|\bWebClient\b", "網路"),
    (r"\bProcess\b", "外部程式"),
    (r"\bThread\b|\bTask\b|\basync\b|\bParallel\b", "執行緒"),
    (r"\bSystem\.Reflection\b|\bActivator\b|\bGetType\(\)\.Get|\bAssembly\b", "反射"),
    (r"\bunsafe\b|\bDllImport\b|\bMarshal\.", "不安全程式碼"),
    (r"\bnew\s+(System\.)?Random\b|\bDateTime\.Now\b|\bGuid\.NewGuid\b|\bEnvironment\.", "破壞固定種子（請用 self.Rng）"),
    (r"\bArena\b|\bAgent\b|\bRoster\b", "碰引擎類別"),
    (r"\bstatic\s+(?!readonly\b|class\b)[\w<>\[\],.]+\s+\w+\s*(=|;)", "static 可變欄位"),
    (r"\bGH_|\bRhinoDoc\b|\bGrasshopper\b", "碰 Rhino／GH 文件"),
]


def strip(text):
    # 去掉註解與字串，避免註解裡寫到關鍵字被誤判
    code = re.sub(r"//.*", "", text)
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    return re.sub(r'@?"(?:\\.|[^"\\])*"', '""', code)


def scan(text):
    code = strip(text)
    return [why for pattern, why in FORBIDDEN if re.search(pattern, code)]


def class_names(text):
    return re.findall(r"\bclass\s+(\w+)", strip(text))


LAST_FAIL = []   # 最近一次試跑失敗時，Smoke 印出的 FAIL／REJECT 行


def build_and_run(files):
    global LAST_FAIL
    LAST_FAIL = []
    items = "\n".join('    <Compile Include="{}" />'.format(f) for f in files)
    (CHECK / "students.g.props").write_text(
        "<Project>\n  <ItemGroup>\n" + items + "\n  </ItemGroup>\n</Project>\n", encoding="utf-8")
    build = subprocess.run(
        ["dotnet", "build", str(CHECK / "check.csproj"), "-v", "q", "-nologo"],
        capture_output=True, text=True, encoding="utf-8", errors="replace")
    if build.returncode != 0:
        errors = [l.strip() for l in build.stdout.splitlines() if "error" in l]
        return False, "編譯錯誤：" + (errors[0][:160] if errors else "未知")
    dll = CHECK / "bin" / "Debug" / "net8.0" / "check.dll"
    try:
        run = subprocess.run(["dotnet", str(dll)], capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=TIMEOUT)
    except subprocess.TimeoutExpired:
        return False, "試跑逾時（可能是無窮迴圈）"
    if run.returncode != 0:
        lines = [l for l in run.stdout.splitlines() if l.startswith(("FAIL", "REJECT"))]
        LAST_FAIL = lines
        tail = run.stderr.strip().splitlines()[-1:] if run.stderr.strip() else []
        return False, "試跑失敗：" + "; ".join(lines + tail)[:200]
    return True, "通過"


def digest(path):
    h = hashlib.sha256()
    for p in sorted((ROOT / "core").glob("*.cs")) + [path]:
        h.update(p.read_bytes())
    return h.hexdigest()


def check_students(students):
    cache = json.loads(CACHE.read_text(encoding="utf-8")) if CACHE.exists() else {}
    passed, report = [], []
    seen_classes = {}
    for path in students:
        if path.name.startswith("boid_template"):   # 範本本來就不參賽（Roster 會跳過），不必檢查
            report.append((path.name, "範本，不載入"))
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        reasons = scan(text)
        if reasons:
            report.append((path.name, "排除：禁用寫法（" + "、".join(reasons) + "）"))
            continue
        clash = [c for c in class_names(text) if c in seen_classes]
        if clash:
            report.append((path.name, "排除：class 名稱和 " + seen_classes[clash[0]] + " 重複（" + clash[0] + "）"))
            continue
        key = digest(path)
        if cache.get(path.name, {}).get("digest") == key:
            ok, why = cache[path.name]["ok"], cache[path.name]["why"]
        else:
            ok, why = build_and_run([path])
            cache[path.name] = {"digest": key, "ok": ok, "why": why}
        if not ok:
            report.append((path.name, "排除：" + why))
            continue
        for c in class_names(text):
            seen_classes[c] = path.name
        passed.append(path)
        report.append((path.name, "通過"))
    CACHE.write_text(json.dumps(cache, ensure_ascii=False, indent=1), encoding="utf-8")

    # 合在一起再編一次。有隊伍在合併試跑時失敗（例如只有遇到很多敵人才會出錯）：直接排除「出錯的那一隊」，
    # 不是排除最後加入的檔；超過 10 隊上限的檔也在這裡被指出來
    if passed:
        ok, why = build_and_run(passed)
        for _ in range(len(passed) + 1):
            if ok or not passed:
                break
            culprits = []
            for line in LAST_FAIL:
                m = re.match(r"(FAIL|REJECT)\s+(Boid_\w+)", line)
                if not m:
                    continue
                for path in passed:
                    text = path.read_text(encoding="utf-8", errors="replace")
                    if m.group(2) in class_names(text) and path not in [c[0] for c in culprits]:
                        reason = ("超過 10 隊上限，不會參賽" if "超過" in line
                                  else "合併試跑時被淘汰（" + line[len(m.group(1)) + 1:][:90] + "）")
                        culprits.append((path, reason))
            if any("上限" not in reason for _, reason in culprits):
                culprits = [c for c in culprits if "上限" not in c[1]]   # 先處理真的出錯的隊，騰出名額後超額的檔可能就能參賽
            if not culprits:
                break
            for path, reason in culprits:
                report.append((path.name, "排除：" + reason))
            passed = [p for p in passed if p not in [c[0] for c in culprits]]
            ok, why = build_and_run(passed) if passed else (True, "")
        if not ok:
            report.append(("（合併）", "合併後失敗：" + why + "，逐一加入找衝突"))
            merged = []
            for path in passed:
                ok, why = build_and_run(merged + [path])
                if ok:
                    merged.append(path)
                else:
                    report.append((path.name, "排除：和前面的檔合併後失敗（" + why + "）"))
            passed = merged
    return passed, report


def main():
    folder = ROOT / "students"
    if "--students" in sys.argv:   # 測試用：改用別的資料夾當學生資料夾
        folder = Path(sys.argv[sys.argv.index("--students") + 1]).resolve()
    students = sorted(p for p in folder.glob("*.cs"))
    report = []
    if "--check" in sys.argv:
        students, report = check_students(students)
    folders = ["core"] + (["examples"] if "--examples" in sys.argv else [])
    files = [p for f in folders for p in sorted((ROOT / f).glob("*.cs"))] + [ROOT / "host" / "GameHost.cs"] + students
    lines = ['#load "{}"'.format(p) for p in files]
    for name, why in report:
        lines.append("// 檢查 {}：{}".format(name, why))
    text = TEMPLATE.read_text(encoding="utf-8").replace("//@LOADS@", "\n".join(lines))
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print("寫入", OUT)
    for name, why in report:
        print(" ", name, why)
    print("載入學生檔", len(students), "個")


if __name__ == "__main__":
    main()
