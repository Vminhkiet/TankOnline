package com.vminhkiet.auth_service.model;

import jakarta.persistence.Embeddable;
import lombok.Data;

@Data
@Embeddable
public class Address {
    private String country;
    private String province;
    private String district;
}
