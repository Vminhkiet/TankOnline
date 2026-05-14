package com.vminhkiet.auth_service.util;

import org.springframework.security.core.GrantedAuthority;

import java.util.Set;
import java.util.HashSet;
import java.util.Collection;

public class CollectionToStringUtil {
    public static String joinAuthorities(Collection<? extends GrantedAuthority> authorities) { 
        Set<String> auths = new HashSet<>();

        for (GrantedAuthority grantedAuthority : authorities) {
            auths.add(grantedAuthority.getAuthority());
        }

        return String.join(",", auths);
    } 
}
