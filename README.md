# LCChineseFont

为 Lethal Company 补上完整的中文字体，带有中文输入法修复。

## 安装

### 自动安装

直接使用 [Gale](https://github.com/Kesomannen/gale)、r2modman 或 Thunderstore Mod Manager 搜索安装。

### 手动安装：

前置：安装 [BepInEx](https://thunderstore.io/c/lethal-company/p/BepInEx/BepInExPack/)（5.4.21 版本）。

1. 从 [Releases](https://github.com/Syrupdesu/LC-ChineseFont/releases) 下载 `LCChineseFont.dll` 和 `FontPatcher.zip`。
2. 将 `LCChineseFont.dll` 放入 `BepInEx/plugins` 文件夹。
3. 解压 `FontPatcher.zip` ，将其放入 `BepInEx/config` 文件夹。

装好之后，你的 `BepInEx` 文件夹应该看起来像这样：

```
BepInEx/
├── plugins/LCChineseFont.dll
└── config/FontPatcher/default/00 zh
```

## 配置

第一次启动后会生成 `BepInEx/config/syrupdesu.lcchinesefont.cfg`：

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `UsingNormalIngameFont` | `true` | 是否使用游戏自带字体。保持 `true` 时，英文使用原字体，缺的字由字体包补上。设为 `false` 则完全交给字体包渲染 |
| `UsingTransmitIngameFont` | `true` | 同上，针对信号翻译器使用的字体 |
| `NormalFontNameRegex` | `^(b\|DialogueText\|3270.*)$` | 匹配需要补字的普通字体的名字 |
| `TransmitFontNameRegex` | `^edunline.*$` | 同上，针对信号翻译器 |
| `FontAssetsPath` | `FontPatcher\default` | 字体包存放路径 |
| `Log` | `false` | 开启或关闭日志 |

## 字体包变体

除了默认的 `00 zh`，`test-bundles/` 里还有两个变体，可以按你喜欢的口味选择：

| 字体包 | 采样 | 滤镜 |
| --- | --- | --- |
| `00 zh`（默认） | 80 pt（5 倍网格） | Point |
| `01 zh bilinear` | 80 pt（5 倍网格） | Bilinear |
| `02 zh native` | 16 pt（1:1 网格） | Point |

添加新字体包：

1. 把想用的字体包放进 `BepInEx/config/FontPatcher/<任意文件夹名>/`。
2. 把配置里的 `FontAssetsPath` 改成 `FontPatcher\<那个文件夹名>`。

## 构建

### 准备

- .NET SDK（用于编译到 net472，即 .NET Framework 4.7.2）
- 游戏自带的程序集目录（`Lethal Company_Data/Managed`），用来编译插件
- 只有重新构建字体包时才需要：Unity **2022.3.62f2**（需安装 Windows Build Support (Mono) 模块）、python3 以及 `fonttools`、`UnityPy`

### 构建插件

```bash
# Debug：可以用 TestDeployPath 指定构建后复制到哪里，方便直接测试
dotnet build -c Debug \
  -p:LethalCompanyDir="/path/to/Lethal Company/Lethal Company_Data/Managed" \
  -p:TestDeployPath="/path/to/Lethal Company/BepInEx/plugins/"

# Release：同时在 build/ 下生成 Thunderstore 压缩包
dotnet build -c Release \
  -p:LethalCompanyDir="/path/to/Lethal Company/Lethal Company_Data/Managed"
```

- `LethalCompanyDir` 默认是 Windows 上的 Steam 路径，在其他系统上需要手动指定。
- 打包优先使用 `tcli`；如果没装或者不能用，会退回到 `tools/package-thunderstore.py`，它会按 `thunderstore.toml` 打出同样的压缩包。想完全跳过打包，加上 `-p:EnableTcliBuild=false`。

### 构建字体包


```bash
tools/build-bundles.sh               # 构建并校验（使用示例字符集）
tools/build-bundles.sh --full-hanzi  # 额外把通用规范汉字表全部加入字体包
```

检查字体对汉字表的覆盖情况：

```bash
python3 tools/check_coverage.py fonts/unifont-18.0.01.otf data/tongyong-guifan-hanzibiao.txt
```

## 常见问题

**装好之后依然显示方框？**

1. 确认模组有被正常加载：查看 `BepInEx/LogOutput.log` ，能否找到 FontPatcher 的相关信息。
2. 确认字体文件确实在 `BepInEx/config/FontPatcher/` 里。
3. 把配置里的 `Log` 改成 `true`，重启游戏。查看看日志里带 `font patched` 和 `[fallback material]` 的几行。
4. 确认无法加载的话，欢迎提出 [Issues](https://github.com/Syrupdesu/LC-ChineseFont/issues) 。


## 鸣谢

- **[LeKAKiD](https://github.com/lekakid)**：原项目 [LC-FontPatcher](https://github.com/lekakid/LC-FontPatcher) 的作者。
- **[GNU Unifont](https://unifoundry.com/unifont/)**：字体包内嵌的字体。
- **[rime-aca/character_set](https://github.com/rime-aca/character_set)**：《通用规范汉字表》的数据来源。
- **[GLM](https://z.ai/)** ：本项目的代码 100% 由 GLM 生成。
- **[Claude](https://claude.ai/)**：协助撰写与润色本 README，并对代码做了审阅。