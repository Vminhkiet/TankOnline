package com.vminhkiet.auth_service.config;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.CommandLineRunner;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Component;

import com.vminhkiet.auth_service.model.Role;
import com.vminhkiet.auth_service.model.User;
import com.vminhkiet.auth_service.repository.UserRepository;

@Component
public class DataSeeder implements CommandLineRunner {

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private PasswordEncoder passwordEncoder;

    @Override
    public void run(String... args) throws Exception {
        if (userRepository.count() == 0) {
            User user1 = new User();
            user1.setUsername("player1");
            user1.setPassword(passwordEncoder.encode("123456"));
            user1.setEmail("player1@tank.com");
            user1.setRole(Role.ROLE_USER);

            User user2 = new User();
            user2.setUsername("player2");
            user2.setPassword(passwordEncoder.encode("123456"));
            user2.setEmail("player2@tank.com");
            user2.setRole(Role.ROLE_USER);

            userRepository.save(user1);
            userRepository.save(user2);

            System.out.println("====== TẠO THÀNH CÔNG 2 TÀI KHOẢN MẪU ======");
            System.out.println("1. player1 / 123456");
            System.out.println("2. player2 / 123456");
            System.out.println("============================================");
        }
    }
}
