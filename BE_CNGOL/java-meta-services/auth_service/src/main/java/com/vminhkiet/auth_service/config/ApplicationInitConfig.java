package com.vminhkiet.auth_service.config;

import org.springframework.boot.ApplicationRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;

import com.vminhkiet.auth_service.model.Role;
import com.vminhkiet.auth_service.model.User;
import com.vminhkiet.auth_service.repository.UserRepository;

@Configuration
public class ApplicationInitConfig {
    @Bean
    ApplicationRunner applicationRunner(UserRepository userRepository) {
        return args -> {
            if (userRepository.existsByRole(Role.ROLE_ADMIN))
                return;
            User admin = new User();
            admin.setUsername("admin");
            admin.setPassword(new BCryptPasswordEncoder().encode("admin123"));
            admin.setRole(Role.ROLE_ADMIN);
            admin.setEmail("minhkietvo6@gmail.com");

            userRepository.save(admin);
            System.out.println("Admin user created: username=admin, password=admin123");
        };
    }
}