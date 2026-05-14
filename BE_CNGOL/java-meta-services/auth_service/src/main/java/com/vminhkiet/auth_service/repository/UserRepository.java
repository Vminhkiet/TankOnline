package com.vminhkiet.auth_service.repository;
import com.vminhkiet.auth_service.model.User;
import com.vminhkiet.auth_service.model.Role;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import java.util.Optional;

@Repository
public interface UserRepository extends JpaRepository<User, Long> {
    Boolean existsByRole(Role role);
    Optional<User> findByUsername(String userName);
    Boolean existsByUsername(String username);
    Boolean existsByEmail(String email);
}
