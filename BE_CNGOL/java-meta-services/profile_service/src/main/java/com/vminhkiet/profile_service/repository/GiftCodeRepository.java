package com.vminhkiet.profile_service.repository;

import com.vminhkiet.profile_service.model.GiftCode;
import jakarta.persistence.LockModeType;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.Optional;

public interface GiftCodeRepository extends JpaRepository<GiftCode, Long> {

    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("SELECT g FROM GiftCode g WHERE g.code = :code")
    Optional<GiftCode> findByCodeForUpdate(@Param("code") String code);

    Optional<GiftCode> findByCode(String code);
}
