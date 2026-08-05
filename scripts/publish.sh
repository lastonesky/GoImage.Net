#!/usr/bin/env bash
# 构建并发布 GoImage NuGet 包到 nuget.org
#
# 用法：
#   ./scripts/publish.sh [版本号] [API Key]
#
# 参数（均可省略，省略时用默认值/环境变量）：
#   版本号    默认 0.1.0；建议显式传入，例如 ./scripts/publish.sh 0.2.0
#   API Key   默认取环境变量 NUGET_API_KEY；为空时只打包不推送
#
# 环境变量：
#   NUGET_API_KEY   nuget.org API Key（或在 https://www.nuget.org/account/apikeys 生成）
#   NUGET_SOURCE    包源，默认 https://api.nuget.org/v3/index.json
#
# 首次发布前需在 nuget.org 注册包所有者 GoImage：
#   https://www.nuget.org/packages/manage/upload

set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:-0.1.0}"
API_KEY="${NUGET_API_KEY:-${2:-}}"
SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
PKG="nupkg/GoImage.$VERSION.nupkg"

echo "==> dotnet pack GoImage.csproj (Version=$VERSION)"
dotnet pack GoImage.csproj -c Release -p:Version="$VERSION" -v minimal

if [ ! -f "$PKG" ]; then
  echo "错误: 未找到 $PKG" >&2
  exit 1
fi
echo "==> 已生成: $PKG"

if [ -z "$API_KEY" ]; then
  echo ""
  echo "未提供 API Key（环境变量 NUGET_API_KEY 或第 2 个参数），跳过推送。"
  echo "要发布到 nuget.org："
  echo "  NUGET_API_KEY=<你的key> ./scripts/publish.sh $VERSION"
  echo "或手动推送："
  echo "  dotnet nuget push $PKG --api-key <你的key> --source $SOURCE"
  exit 0
fi

echo "==> dotnet nuget push -> $SOURCE"
if [ -f "nupkg/GoImage.$VERSION.snupkg" ]; then
  # 同目录符号包会自动随包一起推送到指定符号源
  dotnet nuget push "$PKG" --api-key "$API_KEY" --source "$SOURCE" \
    --symbol-source "$SOURCE" --symbol-api-key "$API_KEY"
else
  dotnet nuget push "$PKG" --api-key "$API_KEY" --source "$SOURCE"
fi
echo "==> 发布完成: GoImage.$VERSION"
