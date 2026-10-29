package main

import (
	"context"
	"errors"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"syscall"
	"time"
)

// config 是服务启动配置，全部可用环境变量覆盖（见 docs/API.md 第 0 节）。
type config struct {
	Port        string // PORT，默认 8080
	DataFile    string // DATA_FILE，默认 ./data/issues.json
	AllowOrigin string // ALLOW_ORIGIN，默认 *
}

// loadConfig 从环境变量读取配置并套用默认值。
func loadConfig() config {
	return config{
		Port:        envOrDefault("PORT", "8080"),
		DataFile:    envOrDefault("DATA_FILE", "./data/issues.json"),
		AllowOrigin: envOrDefault("ALLOW_ORIGIN", "*"),
	}
}

func envOrDefault(key, def string) string {
	if v := strings.TrimSpace(os.Getenv(key)); v != "" {
		return v
	}
	return def
}

// validateConfig 校验配置合法性，避免把明显写错的配置带到启动后。
func validateConfig(cfg config) error {
	port, err := strconv.Atoi(cfg.Port)
	if err != nil || port < 1 || port > 65535 {
		return fmt.Errorf("PORT 必须是 1-65535 之间的整数，当前为 %q", cfg.Port)
	}
	if strings.TrimSpace(cfg.DataFile) == "" {
		return fmt.Errorf("DATA_FILE 不能为空")
	}
	if strings.TrimSpace(cfg.AllowOrigin) == "" {
		return fmt.Errorf("ALLOW_ORIGIN 不能为空")
	}
	return nil
}

func main() {
	logger := log.New(os.Stdout, "", log.LstdFlags|log.Lmicroseconds)

	cfg := loadConfig()
	if err := validateConfig(cfg); err != nil {
		logger.Fatalf("配置不合法: %v", err)
	}

	// 启动时加载持久化数据：文件不存在视为空列表，内容损坏则拒绝启动（避免覆盖丢失数据）。
	store := NewStore(cfg.DataFile)
	if err := store.Load(); err != nil {
		logger.Fatalf("启动加载数据失败: %v", err)
	}

	srv := &http.Server{
		Addr:              ":" + cfg.Port,
		Handler:           NewServer(store, logger, cfg.AllowOrigin).Handler(),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       15 * time.Second,
		WriteTimeout:      30 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	// SIGINT（Ctrl+C）/ SIGTERM 时走优雅关闭。
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	serveErr := make(chan error, 1)
	go func() {
		logger.Printf("AR 巡检后端已启动: http://0.0.0.0:%s （DATA_FILE=%s, ALLOW_ORIGIN=%s）",
			cfg.Port, cfg.DataFile, cfg.AllowOrigin)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			serveErr <- err
		}
	}()

	select {
	case err := <-serveErr:
		logger.Fatalf("服务启动失败: %v", err)
	case <-ctx.Done():
		logger.Printf("收到退出信号，开始优雅关闭...")
	}

	// 给在途请求最多 5 秒处理时间。
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		logger.Printf("优雅关闭超时/失败: %v", err)
	}
	logger.Printf("服务已停止")
}
