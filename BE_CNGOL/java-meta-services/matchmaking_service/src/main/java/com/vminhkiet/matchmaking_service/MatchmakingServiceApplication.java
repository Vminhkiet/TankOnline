package com.vminhkiet.matchmaking_service;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

// @EnableEurekaClient đã bị xóa trong Spring Cloud 2022+
// Eureka client tự động kích hoạt khi có spring-cloud-starter-netflix-eureka-client trên classpath
@SpringBootApplication
public class MatchmakingServiceApplication {

	public static void main(String[] args) {
		SpringApplication.run(MatchmakingServiceApplication.class, args);
	}

}
