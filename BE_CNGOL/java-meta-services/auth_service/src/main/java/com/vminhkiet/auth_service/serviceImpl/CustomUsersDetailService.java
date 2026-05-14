package com.vminhkiet.auth_service.serviceImpl;

import java.util.ArrayList;
import java.util.List;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.core.userdetails.UsernameNotFoundException;
import org.springframework.stereotype.Service;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;

import com.vminhkiet.auth_service.model.User;
import com.vminhkiet.auth_service.model.Role;
import com.vminhkiet.auth_service.repository.UserRepository;

@Service
public class CustomUsersDetailService implements UserDetailsService {
    @Autowired
    private UserRepository userRepository;

    @Override
    public UserDetails loadUserByUsername(String userName) throws UsernameNotFoundException {
        User userQuery = userRepository.findByUsername(userName).orElseThrow(() 
                                    -> new UsernameNotFoundException("User not found"));
        Role role = userQuery.getRole();

        List<GrantedAuthority> authorities = new ArrayList<>();
        authorities.add(new SimpleGrantedAuthority(role.toString()));
        
        return new org.springframework.security.core.userdetails.User(userQuery.getId().toString(), userQuery.getPassword(), authorities);
    }
}
