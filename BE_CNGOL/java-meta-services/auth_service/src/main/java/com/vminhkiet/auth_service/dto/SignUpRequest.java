package com.vminhkiet.auth_service.dto;

import com.vminhkiet.auth_service.model.Address;
import lombok.Data;

@Data
public class SignUpRequest {
    private String username;
    private String password;
    private String email;
    private Address address;
}
