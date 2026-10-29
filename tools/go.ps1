# 统一 Go 调用封装：使用工作区内的 Go 工具链与缓存目录（沙箱不允许写用户目录）
$env:GOROOT = 'D:\项目\面试测试\.tools\go'
$env:GOPATH = 'D:\项目\面试测试\.tools\gopath'
$env:GOCACHE = 'D:\项目\面试测试\.tools\gocache'
$env:GOTOOLCHAIN = 'local'
$env:GOPROXY = 'off'
$env:CGO_ENABLED = '0'
& "$env:GOROOT\bin\go.exe" @args
exit $LASTEXITCODE
