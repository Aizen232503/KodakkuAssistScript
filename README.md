# KodakkuAssistScript
My scripts for Kodakku Assist, a Dalamud plugin of Final Fantasy XIV.

## 更新在线索引

安装 Python 3 后，双击 `update.bat` 即可更新 `OnlineRepo.json`。有改动的公开脚本会列出版本变化：按回车将版本号最后一位加一，输入 `0` 则只更新索引。没有需要递增的版本时直接完成。工具不需要 .NET，不会提交或推送 Git。

首次运行会建立当前内容记录，不自动递增版本。后续按脚本 GUID 比较内容及路径：重复运行不会连续涨版本；已经手动提高版本的脚本也不会再加一次。新收录的脚本沿用其初始版本，未公开的脚本不参与递增。

`utils/update-state.json` 保存本机上次处理的内容记录，已加入 Git 忽略规则。删除它会在下次运行时重新建立记录。输入 `0` 同样会记录本次内容，因此这次改动不会在下次运行时再次提示递增。

脚本默认进入索引。不想收录时，在脚本开头（`using` 之前）添加：

```csharp
// OnlineRepo: false
```

删除该注释或改成 `// OnlineRepo: true` 即可恢复收录。此开关只控制在线索引，不控制 Git 是否跟踪或上传源文件。

`utils/config.json` 设置 GitHub 仓库名和分支。生成器递归读取实际脚本路径，生成可直接下载的 Raw 地址，跳过 `.git`、`.vs`、`bin`、`obj` 和 `utils` 目录。

`utils/parser.py` 读取 C# 的 `ScriptType` 元数据，不运行或编译脚本。它支持普通/逐字字符串、字符串常量及拼接、整数常量、带注释的地图列表；不支持的元数据表达式会报错。失败时旧索引保持不变。

命令行或其他批处理可以使用 `update.bat --no-pause`，跳过版本询问和暂停，仅更新索引及内容记录；直接运行 `python utils/parser.py` 效果相同。需要交互版本提示时使用 `python utils/parser.py --interactive`。所有公开脚本解析和版本检查通过后才会写入；写入失败时恢复已替换的文件。

要让生成的下载地址生效，需要自行将要公开的脚本和索引提交、推送到配置中的仓库分支。
