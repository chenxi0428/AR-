# 统一 npm 调用封装：缓存目录放在工作区内（沙箱不允许写用户目录）
$env:npm_config_cache = 'D:\项目\面试测试\.npm-cache'
$env:npm_config_fund = 'false'
$env:npm_config_audit = 'false'
& npm.cmd @args
exit $LASTEXITCODE
