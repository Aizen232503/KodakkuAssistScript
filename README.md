# KodakkuAssistScript
My scripts for Kodakku Assist, a Dalamud plugin of Final Fantasy XIV.

## 更新在线索引

安装 Python 3 后，双击 `update.bat` 即可重新生成 `OnlineRepo.json`。工具不需要 .NET，不会提交或推送 Git。

脚本默认进入索引。不想收录时，在脚本开头（`using` 之前）添加：

```csharp
// OnlineRepo: false
```

删除该注释或改成 `// OnlineRepo: true` 即可恢复收录。此开关只控制在线索引，不控制 Git 是否跟踪或上传源文件。

`utils/config.json` 设置 GitHub 仓库名和分支。生成器递归读取实际脚本路径，生成可直接下载的 Raw 地址，跳过 `.git`、`.vs`、`bin`、`obj` 和 `utils` 目录。

`utils/parser.py` 读取 C# 的 `ScriptType` 元数据，不运行或编译脚本。它支持普通/逐字字符串、字符串常量及拼接、整数常量、带注释的地图列表；不支持的元数据表达式会报错。失败时旧索引保持不变。

命令行或其他批处理可以使用 `update.bat --no-pause`；也可以直接运行 `python utils/parser.py`。

要让生成的下载地址生效，需要自行将要公开的脚本和索引提交、推送到配置中的仓库分支。
