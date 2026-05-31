package com.vminhkiet.auth_service.serviceImpl;

import com.fasterxml.jackson.databind.ObjectMapper;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Service;
import org.springframework.web.server.ResponseStatusException;
import org.springframework.http.HttpStatus;

import java.util.Map;

import com.vminhkiet.auth_service.config.JwtProvider;
import com.vminhkiet.auth_service.dto.AuthResponse;
import com.vminhkiet.auth_service.dto.LoginRequest;
import com.vminhkiet.auth_service.dto.SignUpRequest;
import com.vminhkiet.auth_service.dto.UserMeResponse;
import com.vminhkiet.auth_service.model.Role;
import com.vminhkiet.auth_service.model.User;
import com.vminhkiet.auth_service.repository.UserRepository;
import com.vminhkiet.auth_service.util.CollectionToStringUtil;

import java.util.List;

@Service
public class UserService implements com.vminhkiet.auth_service.service.UserService{
    @Autowired
    private UserRepository userRepository;
    @Autowired
    private PasswordEncoder passwordEncoder;
    @Autowired
    private CustomUsersDetailService customUsersDetailService;
    @Autowired
    private JwtProvider jwtProvider;
    @Autowired
    private SessionService sessionService;

    @Autowired
    private KafkaTemplate<String, String> kafkaTemplate;

    @Autowired
    private SessionInvalidationProducer sessionInvalidationProducer;

    private final ObjectMapper objectMapper = new ObjectMapper();

    @Override
    public AuthResponse loginAccount(LoginRequest request) {
        String userName = request.getUsername();
        String passWord = request.getPassword();

        Authentication auth = authenticate(userName, passWord);

        String roles = CollectionToStringUtil.joinAuthorities(auth.getAuthorities());
        String refreshToken = sessionService.login(Long.parseLong(auth.getName()), roles);

        String jwt = jwtProvider.generateAccessToken(auth.getName(), roles);
                
        AuthResponse authResponse = new AuthResponse();
        authResponse.setJwt(jwt);
        authResponse.setRefreshToken(refreshToken);
        return authResponse;
    }

    private Authentication authenticate(String userName, String passWord) {
        UserDetails userDetails  = customUsersDetailService.loadUserByUsername(userName);

        if (!passwordEncoder.matches(passWord, userDetails.getPassword()))
            throw new BadCredentialsException("Invalid username or password");

        // The id is used as the username in the UserDetails
        Long userId = Long.parseLong(userDetails.getUsername());
        User user = userRepository.findById(userId).orElseThrow();
        if (Boolean.TRUE.equals(user.getIsBanned())) {
            throw new ResponseStatusException(HttpStatus.FORBIDDEN, "Tài khoản của bạn đã bị cấm.");
        }

        Authentication authObj = new UsernamePasswordAuthenticationToken(userDetails, null, userDetails.getAuthorities());
        return authObj;
    }

    @Override
    public AuthResponse registerAccount(SignUpRequest request) {
        if (userRepository.existsByUsername(request.getUsername()))
            throw new IllegalArgumentException("Username '" + request.getUsername() + "' đã tồn tại");

        if (userRepository.existsByEmail(request.getEmail()))
            throw new IllegalArgumentException("Email '" + request.getEmail() + "' đã được đăng ký");

        User newUser = new User();
        newUser.setUsername(request.getUsername());
        newUser.setPassword(passwordEncoder.encode(request.getPassword()));
        newUser.setEmail(request.getEmail());
        newUser.setAddress(request.getAddress());
        newUser.setRole(Role.ROLE_USER);

        User savedUser = userRepository.save(newUser);

        // Publish event user.created lên Kafka để profile_service tạo profile
        try {
            String payload = objectMapper.writeValueAsString(Map.of(
                "userId",      String.valueOf(savedUser.getId()),
                "displayName", savedUser.getUsername()
            ));
            kafkaTemplate.send("user.created", String.valueOf(savedUser.getId()), payload);
        } catch (Exception e) {
            // Kafka không khả dụng — không block signup
        }

        // Tự động login sau khi đăng ký thành công
        Authentication auth = authenticate(request.getUsername(), request.getPassword());
        String roles = CollectionToStringUtil.joinAuthorities(auth.getAuthorities());
        String refreshToken = sessionService.login(Long.parseLong(auth.getName()), roles);
        String jwt = jwtProvider.generateAccessToken(auth.getName(), roles);

        AuthResponse response = new AuthResponse();
        response.setJwt(jwt);
        response.setRefreshToken(refreshToken);
        return response;
    }

    @Override
    public UserMeResponse getUserMe(Long userId) {
        User user = userRepository.findById(userId)
                .orElseThrow(() -> new IllegalArgumentException("User not found"));
        UserMeResponse response = new UserMeResponse();
        response.setUserId(String.valueOf(user.getId()));
        response.setUsername(user.getUsername());
        response.setEmail(user.getEmail());
        return response;
    }

    @Override
    public List<User> getAllUser(){
        return userRepository.findAll();
    }

    @Override
    public void toggleBan(Long userId) {
        User user = userRepository.findById(userId)
                .orElseThrow(() -> new IllegalArgumentException("User not found"));
        user.setIsBanned(!Boolean.TRUE.equals(user.getIsBanned()));
        userRepository.save(user);

        if (Boolean.TRUE.equals(user.getIsBanned())) {
            sessionService.logout(userId);
            sessionInvalidationProducer.publishBanKick(userId);
        }
    }
}
