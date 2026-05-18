package com.vminhkiet.auth_service.dto;

import lombok.Data;

@Data
public class UserMeResponse {
    private String userId;
    private String username;
    private String email;
}
