# KodakkuAssistScript

本仓库收录用于 Final Fantasy XIV 插件 **Kodakku Assist** 的脚本，并提供在线索引维护工具，支持自动更新索引、递增脚本版本号及选择性收录。

## 在线索引更新

工具依赖 **Python 3.x**。双击 `update.bat`，即可自动生成或更新 `OnlineRepo.json`。检测到脚本改动时，按回车将版本号最后一位加一，输入 `0` 则仅更新索引。首次运行只建立记录，不自动递增版本。

## 选择性收录

脚本默认收录到在线索引。在脚本文件开头、`using` 之前添加 `// OnlineRepo: false`，即可排除该脚本。例如：

```csharp
// OnlineRepo: false

using System;
// 其余脚本内容……
```

删除该注释，或改为 `// OnlineRepo: true`，即可恢复收录。
