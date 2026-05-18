package com.vminhkiet.auth_service.service;

import com.vminhkiet.auth_service.dto.AuthResponse;
import com.vminhkiet.auth_service.dto.LoginRequest;
import com.vminhkiet.auth_service.dto.SignUpRequest;
import com.vminhkiet.auth_service.dto.UserMeResponse;
import com.vminhkiet.auth_service.model.User;

import java.util.List;

public interface UserService {
    AuthResponse loginAccount(LoginRequest request);
    AuthResponse registerAccount(SignUpRequest request);
    UserMeResponse getUserMe(Long userId);
    List<User> getAllUser();
}
