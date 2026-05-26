package com.vminhkiet.shop_service.config;

import com.vminhkiet.shop_service.model.Item;
import com.vminhkiet.shop_service.repository.ItemRepository;
import org.springframework.boot.CommandLineRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.math.BigDecimal;
import java.util.Arrays;

@Configuration
public class DataSeeder {

    @Bean
    public CommandLineRunner initDatabase(ItemRepository itemRepository) {
        return args -> {
            if (itemRepository.count() == 0) {
                Item bulldog = Item.builder()
                        .name("BULLDOG")
                        .price(new BigDecimal("600"))
                        .description("Standard combat tank with balanced performance. Reliable across a wide range of battlefield scenarios.")
                        .category("TANK")
                        .availble(true)
                        .imageUrl("")
                        .damage(7).armor(4).speed(6).health(100).fireRange(6).fireRate(4)
                        .build();

                Item crabX = Item.builder()
                        .name("CRAB-X")
                        .price(new BigDecimal("1000"))
                        .description("Multi-legged assault platform built for area control. Maintains stability under sustained combat conditions.")
                        .category("TANK")
                        .availble(true)
                        .imageUrl("")
                        .damage(8).armor(6).speed(3).health(100).fireRange(6).fireRate(3)
                        .build();

                Item scout01 = Item.builder()
                        .name("SCOUT-01")
                        .price(new BigDecimal("2000"))
                        .description("Light reconnaissance unit optimized for speed and mobility. Designed for rapid engagement and tactical repositioning.Maintains stability under sustained combat conditions.")
                        .category("TANK")
                        .availble(true)
                        .imageUrl("")
                        .damage(4).armor(3).speed(10).health(100).fireRange(6).fireRate(10)
                        .build();

                Item sentinelX = Item.builder()
                        .name("SENTINEL-X")
                        .price(new BigDecimal("4000"))
                        .description("Humanoid combat unit designed for close-range engagement. Combines mobility with high-pressure offensive capability.")
                        .category("TANK")
                        .availble(true)
                        .imageUrl("")
                        .damage(10).armor(7).speed(4).health(100).fireRange(5).fireRate(4)
                        .build();

                Item titan816 = Item.builder()
                        .name("TITAN-816")
                        .price(new BigDecimal("6000"))
                        .description("Advanced heavy tank equipped with dual rail cannons. Delivers high-impact firepower with reinforced frontal armor.")
                        .category("TANK")
                        .availble(true)
                        .imageUrl("")
                        .damage(9).armor(9).speed(3).health(100).fireRange(3).fireRate(8)
                        .build();

                itemRepository.saveAll(Arrays.asList(bulldog, crabX, scout01, sentinelX, titan816));
                System.out.println("Default tanks have been seeded into the database.");
            }
        };
    }
}
